// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// Core loop for a <see cref="VoiceAgent"/>: consumes streaming speech-to-text, drives the reasoning
/// <see cref="IChatClient"/> per user turn, buffers assistant text into clauses, sends completed
/// clauses to text-to-speech, and funnels everything through one single-reader channel so the caller
/// sees one ordered <see cref="VoiceAgentUpdate"/> stream.
/// </summary>
/// <remarks>
/// Two producers write to the output channel: the STT consumer (transcripts) and the reasoning worker
/// (turn lifecycle plus assistant text and audio). When barge-in is enabled, a qualifying STT partial
/// arriving during an active turn cancels that turn's per-turn token; the reasoning worker observes the
/// cancellation, emits <see cref="VoiceAgentUpdateKind.Cancelled"/>, and moves on to the next turn.
/// </remarks>
internal static class VoiceAgentPipeline
{
    public static IAsyncEnumerable<VoiceAgentUpdate> RunAsync(
        ISpeechToTextClient stt, IChatClient chat, ITextToSpeechClient tts,
        VoiceAgentOptions options, Stream microphonePcm, CancellationToken cancellationToken) =>
        new Runner(stt, chat, tts, options).RunAsync(microphonePcm, cancellationToken);

    private sealed class Runner
    {
        private readonly ISpeechToTextClient _stt;
        private readonly IChatClient _chat;
        private readonly ITextToSpeechClient _tts;
        private readonly VoiceAgentOptions _options;

        // Guards the current turn's cancellation source so the STT consumer can interrupt the reasoning
        // worker's in-flight turn without a data race between the two producer tasks.
        private readonly object _turnLock = new();
        private CancellationTokenSource? _activeTurnCts;

        public Runner(ISpeechToTextClient stt, IChatClient chat, ITextToSpeechClient tts, VoiceAgentOptions options)
        {
            _stt = stt;
            _chat = chat;
            _tts = tts;
            _options = options;
        }

        public async IAsyncEnumerable<VoiceAgentUpdate> RunAsync(
            Stream microphonePcm, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = linkedCts.Token;

            var output = Channel.CreateUnbounded<VoiceAgentUpdate>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
            var userTurns = Channel.CreateUnbounded<string>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

            var history = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(_options.Instructions))
            {
                history.Add(new ChatMessage(ChatRole.System, _options.Instructions));
            }

            var sttTask = Task.Run(() => StreamTranscriptsAsync(microphonePcm, output.Writer, userTurns.Writer, token), token);
            var reasoningTask = Task.Run(() => DriveTurnsAsync(history, userTurns.Reader, output.Writer, token), token);

            Exception? failure = null;
            try
            {
                while (await output.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (output.Reader.TryRead(out var update))
                    {
                        yield return update;
                    }
                }
            }
            finally
            {
                linkedCts.Cancel();
                // Await the reasoning worker first so its root failure wins over any secondary teardown
                // exception (a cancellation/ChannelClosedException) that awaiting STT first could capture.
                failure = await AwaitAndCaptureAsync(reasoningTask, failure).ConfigureAwait(false);
                failure = await AwaitAndCaptureAsync(sttTask, failure).ConfigureAwait(false);
            }

            if (failure is not null and not OperationCanceledException)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private async Task StreamTranscriptsAsync(
            Stream microphonePcm, ChannelWriter<VoiceAgentUpdate> output, ChannelWriter<string> userTurns, CancellationToken token)
        {
            try
            {
                var sttOptions = new SpeechToTextOptions
                {
                    SpeechLanguage = _options.Language,
                    SpeechSampleRate = _options.InputSampleRateHertz,
                };

                await foreach (var update in _stt.GetStreamingTextAsync(microphonePcm, sttOptions, token).ConfigureAwait(false))
                {
                    if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdating)
                    {
                        await output.WriteAsync(
                            new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptPartial, Text = update.Text, IsFinal = false },
                            token).ConfigureAwait(false);

                        if (_options.EnableBargeIn && BargeInDetector.ShouldInterrupt(update.Text))
                        {
                            InterruptActiveTurn();
                        }
                    }
                    else if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdated)
                    {
                        var text = update.Text ?? string.Empty;
                        await output.WriteAsync(
                            new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptFinal, Text = text, IsFinal = true },
                            token).ConfigureAwait(false);

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            await userTurns.WriteAsync(text, token).ConfigureAwait(false);
                        }
                    }
                    else if (update.Kind == SpeechToTextResponseUpdateKind.Error)
                    {
                        // Resilience (reconnect/resume) is a non-goal, but a stream failure is a real error:
                        // propagate it so RunAsync rethrows a non-cancellation failure instead of ending the
                        // voice loop as if the user simply stopped talking.
                        if (update.RawRepresentation is Exception ex)
                        {
                            ExceptionDispatchInfo.Capture(ex).Throw();
                        }

                        throw new InvalidOperationException(
                            string.IsNullOrEmpty(update.Text) ? "The speech-to-text stream reported an error." : update.Text);
                    }
                }
            }
            finally
            {
                userTurns.TryComplete();
            }
        }

        // Cancels the reasoning worker's in-flight turn, if any. Called from the STT consumer.
        private void InterruptActiveTurn()
        {
            lock (_turnLock)
            {
                var cts = _activeTurnCts;
                if (cts is not null && !cts.IsCancellationRequested)
                {
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { /* turn finished between the null check and Cancel */ }
                }
            }
        }

        private async Task DriveTurnsAsync(
            List<ChatMessage> history, ChannelReader<string> userTurns,
            ChannelWriter<VoiceAgentUpdate> output, CancellationToken loopToken)
        {
            try
            {
                while (await userTurns.WaitToReadAsync(loopToken).ConfigureAwait(false))
                {
                    while (userTurns.TryRead(out var userText))
                    {
                        await ProcessTurnAsync(history, userText, output, loopToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                output.TryComplete();
            }
        }

        private async Task ProcessTurnAsync(
            List<ChatMessage> history, string userText,
            ChannelWriter<VoiceAgentUpdate> output, CancellationToken loopToken)
        {
            history.Add(new ChatMessage(ChatRole.User, userText));

            using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(loopToken);
            lock (_turnLock) { _activeTurnCts = turnCts; }
            var turnToken = turnCts.Token;

            var accumulated = new StringBuilder();
            string? responseId = null;
            UsageDetails? lastUsage = null;
            bool cancelled = false;

            try
            {
                await output.WriteAsync(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnStarted }, loopToken)
                    .ConfigureAwait(false);

                var chatOptions = new ChatOptions { ModelId = _options.ModelId, Tools = _options.Tools };
                var textBuffer = new StringBuilder();

                await foreach (var chunk in _chat.GetStreamingResponseAsync(history, chatOptions, turnToken).ConfigureAwait(false))
                {
                    responseId ??= chunk.ResponseId;
                    foreach (var usage in chunk.Contents.OfType<UsageContent>())
                    {
                        lastUsage = usage.Details;
                    }

                    var delta = chunk.Text;
                    if (string.IsNullOrEmpty(delta)) continue;

                    textBuffer.Append(delta);
                    accumulated.Append(delta);

                    // Turn-owned output is gated on the per-turn token so a barge-in interrupt stops the
                    // interrupted turn from publishing further assistant text after the interrupting partial.
                    await output.WriteAsync(
                        new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantText, Text = delta, ResponseId = responseId },
                        turnToken).ConfigureAwait(false);

                    int flush;
                    while ((flush = ClauseChunker.NextClauseBoundary(textBuffer)) > 0)
                    {
                        var clause = textBuffer.ToString(0, flush).Trim();
                        textBuffer.Remove(0, flush);
                        if (clause.Length > 0)
                        {
                            await SpeakClauseAsync(clause, output, responseId, turnToken).ConfigureAwait(false);
                        }
                    }
                }

                if (textBuffer.Length > 0)
                {
                    var tail = textBuffer.ToString().Trim();
                    if (tail.Length > 0)
                    {
                        await SpeakClauseAsync(tail, output, responseId, turnToken).ConfigureAwait(false);
                    }
                }

                // A barge-in can cancel the turn after the final provider iteration without either provider
                // throwing; check the per-turn token before committing so a late interrupt still routes to the
                // Cancelled path instead of emitting TurnComplete.
                if (turnToken.IsCancellationRequested)
                {
                    cancelled = true;
                }
                else
                {
                    history.Add(new ChatMessage(ChatRole.Assistant, accumulated.ToString()));
                    await output.WriteAsync(
                        new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnComplete, Usage = lastUsage, ResponseId = responseId },
                        loopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (turnToken.IsCancellationRequested && !loopToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            finally
            {
                lock (_turnLock)
                {
                    if (ReferenceEquals(_activeTurnCts, turnCts)) _activeTurnCts = null;
                }
            }

            if (cancelled)
            {
                // Keep the partial reply the caller already heard so conversation state stays coherent.
                if (accumulated.Length > 0)
                {
                    history.Add(new ChatMessage(ChatRole.Assistant, accumulated.ToString()));
                }
                await output.WriteAsync(
                    new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.Cancelled, ResponseId = responseId },
                    loopToken).ConfigureAwait(false);
            }
        }

        private async Task SpeakClauseAsync(
            string clause, ChannelWriter<VoiceAgentUpdate> output, string? responseId, CancellationToken turnToken)
        {
            var ttsOptions = new TextToSpeechOptions { VoiceId = _options.Voice.Value };

            await foreach (var update in _tts.GetStreamingAudioAsync(clause, ttsOptions, turnToken).ConfigureAwait(false))
            {
                foreach (var content in update.Contents.OfType<DataContent>())
                {
                    // Turn-owned audio is gated on the per-turn token: once the turn is interrupted, an
                    // already-fetched audio chunk is dropped rather than published after the user's barge-in.
                    await output.WriteAsync(
                        new VoiceAgentUpdate
                        {
                            Kind = VoiceAgentUpdateKind.AssistantAudio,
                            Audio = content.Data,
                            ResponseId = responseId,
                        },
                        turnToken).ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask<Exception?> AwaitAndCaptureAsync(Task task, Exception? first)
        {
            try
            {
                await task.ConfigureAwait(false);
                return first;
            }
            catch (Exception ex)
            {
                return first ?? ex;
            }
        }
    }
}
#endif

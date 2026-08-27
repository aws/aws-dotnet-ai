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
/// (turn lifecycle plus assistant text and audio). Barge-in is not yet wired: the reasoning worker
/// completes the current turn before starting the next.
/// </remarks>
internal static class VoiceAgentPipeline
{
    public static async IAsyncEnumerable<VoiceAgentUpdate> RunAsync(
        ISpeechToTextClient stt, IChatClient chat, ITextToSpeechClient tts,
        VoiceAgentOptions options, Stream microphonePcm,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linkedCts.Token;

        // Single-reader output funnel: two producer tasks write, the caller's foreach drains.
        var output = Channel.CreateUnbounded<VoiceAgentUpdate>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // The STT consumer enqueues each finalized user utterance; the reasoning worker drains it.
        var userTurns = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // Conversation history is owned by the reasoning worker; both readers see it single-threaded.
        var history = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(options.Instructions))
        {
            history.Add(new ChatMessage(ChatRole.System, options.Instructions));
        }

        var sttTask = Task.Run(() => StreamTranscriptsAsync(stt, microphonePcm, options, output.Writer, userTurns.Writer, token), token);
        var reasoningTask = Task.Run(() => DriveTurnsAsync(chat, tts, options, history, userTurns.Reader, output.Writer, token), token);

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
            failure = await AwaitAndCaptureAsync(sttTask, failure).ConfigureAwait(false);
            failure = await AwaitAndCaptureAsync(reasoningTask, failure).ConfigureAwait(false);
        }

        // Cancellation is expected on caller-driven teardown; anything else is a real failure.
        if (failure is not null and not OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task StreamTranscriptsAsync(
        ISpeechToTextClient stt, Stream microphonePcm, VoiceAgentOptions options,
        ChannelWriter<VoiceAgentUpdate> output, ChannelWriter<string> userTurns, CancellationToken token)
    {
        try
        {
            var sttOptions = new SpeechToTextOptions
            {
                SpeechLanguage = options.Language,
                SpeechSampleRate = options.InputSampleRateHertz,
            };

            await foreach (var update in stt.GetStreamingTextAsync(microphonePcm, sttOptions, token).ConfigureAwait(false))
            {
                if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdating)
                {
                    await output.WriteAsync(
                        new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptPartial, Text = update.Text, IsFinal = false },
                        token).ConfigureAwait(false);
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
                    // Resilience is a non-goal; a stream failure ends the STT half of the loop.
                    break;
                }
            }
        }
        finally
        {
            userTurns.TryComplete();
        }
    }

    private static async Task DriveTurnsAsync(
        IChatClient chat, ITextToSpeechClient tts, VoiceAgentOptions options,
        List<ChatMessage> history, ChannelReader<string> userTurns,
        ChannelWriter<VoiceAgentUpdate> output, CancellationToken token)
    {
        try
        {
            while (await userTurns.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (userTurns.TryRead(out var userText))
                {
                    await ProcessTurnAsync(chat, tts, options, history, userText, output, token).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            output.TryComplete();
        }
    }

    private static async Task ProcessTurnAsync(
        IChatClient chat, ITextToSpeechClient tts, VoiceAgentOptions options,
        List<ChatMessage> history, string userText,
        ChannelWriter<VoiceAgentUpdate> output, CancellationToken token)
    {
        history.Add(new ChatMessage(ChatRole.User, userText));

        await output.WriteAsync(
            new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnStarted },
            token).ConfigureAwait(false);

        var chatOptions = new ChatOptions
        {
            ModelId = options.ModelId,
            Tools = options.Tools,
        };

        var accumulated = new StringBuilder();
        var textBuffer = new StringBuilder();
        string? responseId = null;
        UsageDetails? lastUsage = null;

        await foreach (var chunk in chat.GetStreamingResponseAsync(history, chatOptions, token).ConfigureAwait(false))
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

            await output.WriteAsync(
                new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantText, Text = delta, ResponseId = responseId },
                token).ConfigureAwait(false);

            int flush;
            while ((flush = ClauseChunker.NextClauseBoundary(textBuffer)) > 0)
            {
                var clause = textBuffer.ToString(0, flush).Trim();
                textBuffer.Remove(0, flush);
                if (clause.Length > 0)
                {
                    await SpeakClauseAsync(tts, clause, options, output, responseId, token).ConfigureAwait(false);
                }
            }
        }

        // Flush any tail text with no trailing punctuation as a final clause.
        if (textBuffer.Length > 0)
        {
            var tail = textBuffer.ToString().Trim();
            textBuffer.Clear();
            if (tail.Length > 0)
            {
                await SpeakClauseAsync(tts, tail, options, output, responseId, token).ConfigureAwait(false);
            }
        }

        history.Add(new ChatMessage(ChatRole.Assistant, accumulated.ToString()));

        await output.WriteAsync(
            new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnComplete, Usage = lastUsage, ResponseId = responseId },
            token).ConfigureAwait(false);
    }

    private static async Task SpeakClauseAsync(
        ITextToSpeechClient tts, string clause, VoiceAgentOptions options,
        ChannelWriter<VoiceAgentUpdate> output, string? responseId, CancellationToken token)
    {
        var ttsOptions = new TextToSpeechOptions
        {
            VoiceId = options.Voice.Value,
        };

        await foreach (var update in tts.GetStreamingAudioAsync(clause, ttsOptions, token).ConfigureAwait(false))
        {
            foreach (var content in update.Contents.OfType<DataContent>())
            {
                await output.WriteAsync(
                    new VoiceAgentUpdate
                    {
                        Kind = VoiceAgentUpdateKind.AssistantAudio,
                        Audio = content.Data,
                        ResponseId = responseId,
                    },
                    token).ConfigureAwait(false);
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
#endif

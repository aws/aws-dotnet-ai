// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI;

public class VoiceAgentBargeInTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_BargeInEnabled_PartialDuringTurn_CancelsAndStartsNewTurn()
    {
        // STT scripts turn 1's final, waits for the test to release the gate, then a qualifying partial
        // (barge-in) followed by turn 2's final. The gate is released once the caller observes TurnStarted.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stt = new GatedStt(gate);
        var chat = new BlockingFirstTurnChat();
        var tts = new EchoTts();

        var agent = new VoiceAgent(stt, chat, tts, new VoiceAgentOptions { EnableBargeIn = true });
        using var mic = new MemoryStream(new byte[] { 0 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var updates = new List<VoiceAgentUpdate>();
        bool released = false;
        await foreach (var update in agent.RunAsync(mic, timeout.Token))
        {
            updates.Add(update);
            if (!released && update.Kind == VoiceAgentUpdateKind.TurnStarted)
            {
                released = true;
                gate.SetResult();   // let the interrupting partial flow
            }
        }

        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.Cancelled);

        // Turn 1 was cancelled before it could complete; the only TurnComplete belongs to turn 2.
        int cancelledIdx = updates.FindIndex(u => u.Kind == VoiceAgentUpdateKind.Cancelled);
        int firstCompleteIdx = updates.FindIndex(u => u.Kind == VoiceAgentUpdateKind.TurnComplete);
        Assert.True(firstCompleteIdx < 0 || cancelledIdx < firstCompleteIdx,
            "The cancelled turn must not have produced a TurnComplete before the barge-in.");

        // The barge-in path drove a second turn to completion.
        Assert.Equal(2, chat.CallCount);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.TurnComplete);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_BargeInDisabled_QualifyingPartialDuringTurn_DoesNotCancel()
    {
        // A *qualifying* partial (>= BargeInDetector threshold) arrives while turn 1 is in flight, but
        // barge-in is disabled, so it must NOT cancel the turn. Turn 1 is gated to complete only after that
        // partial has been delivered, which proves the partial was observed mid-turn yet ignored. If a
        // regression made the pipeline ignore EnableBargeIn, the partial would cancel the turn and the chat's
        // wait would throw, producing a Cancelled update and failing the assertions below.
        var turnStartedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partialDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stt = new PartialDuringTurnStt(turnStartedGate, partialDelivered);
        var chat = new GatedCompletionChat(partialDelivered);
        var tts = new EchoTts();

        var agent = new VoiceAgent(stt, chat, tts, new VoiceAgentOptions { EnableBargeIn = false });
        using var mic = new MemoryStream(new byte[] { 0 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var updates = new List<VoiceAgentUpdate>();
        bool released = false;
        await foreach (var update in agent.RunAsync(mic, timeout.Token))
        {
            updates.Add(update);
            if (!released && update.Kind == VoiceAgentUpdateKind.TurnStarted)
            {
                released = true;
                turnStartedGate.SetResult();   // let the interrupting partial flow during the active turn
            }
        }

        Assert.DoesNotContain(updates, u => u.Kind == VoiceAgentUpdateKind.Cancelled);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.TurnComplete);
        Assert.Equal(1, chat.CallCount);   // the ignored partial never started a second turn
    }

    // ---- test doubles ----

    private sealed class GatedStt : ISpeechToTextClient
    {
        private readonly TaskCompletionSource _gate;
        public GatedStt(TaskCompletionSource gate) => _gate = gate;

        public Task<SpeechToTextResponse> GetTextAsync(Stream a, SpeechToTextOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream, SpeechToTextOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new SpeechToTextResponseUpdate("first question") { Kind = SpeechToTextResponseUpdateKind.TextUpdated };

            // Wait until the caller has seen turn 1 start, then interrupt it.
            await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            yield return new SpeechToTextResponseUpdate("actually wait") { Kind = SpeechToTextResponseUpdateKind.TextUpdating };
            yield return new SpeechToTextResponseUpdate("actually wait") { Kind = SpeechToTextResponseUpdateKind.TextUpdated };
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class PartialDuringTurnStt : ISpeechToTextClient
    {
        private readonly TaskCompletionSource _turnStarted;
        private readonly TaskCompletionSource _partialDelivered;

        public PartialDuringTurnStt(TaskCompletionSource turnStarted, TaskCompletionSource partialDelivered)
        {
            _turnStarted = turnStarted;
            _partialDelivered = partialDelivered;
        }

        public Task<SpeechToTextResponse> GetTextAsync(Stream a, SpeechToTextOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream, SpeechToTextOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new SpeechToTextResponseUpdate("first question") { Kind = SpeechToTextResponseUpdateKind.TextUpdated };

            // Wait until the caller has seen turn 1 start, then emit a qualifying partial *during* the turn.
            await _turnStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            yield return new SpeechToTextResponseUpdate("actually wait") { Kind = SpeechToTextResponseUpdateKind.TextUpdating };
            _partialDelivered.TrySetResult();
            // No second final utterance: the partial alone must not start or cancel a turn.
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class BlockingFirstTurnChat : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Working on it. ");
                // Block until the turn's token is cancelled by barge-in.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "Second answer.");
            await Task.Yield();
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class GatedCompletionChat : IChatClient
    {
        private readonly TaskCompletionSource _proceed;
        public int CallCount { get; private set; }

        public GatedCompletionChat(TaskCompletionSource proceed) => _proceed = proceed;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Working on it. ");

            // Complete only after the interrupting partial has been delivered, so the partial is guaranteed
            // to have been processed mid-turn. With barge-in disabled the per-turn token is never cancelled,
            // so this wait completes normally rather than throwing.
            await _proceed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            yield return new ChatResponseUpdate(ChatRole.Assistant, "Done.");
            await Task.Yield();
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class EchoTts : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(string t, TextToSpeechOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text, TextToSpeechOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            yield return new TextToSpeechResponseUpdate(new List<AIContent> { new DataContent(bytes, "audio/lpcm") })
            {
                Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
            };
            await Task.Yield();
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
#endif

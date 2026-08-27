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
    public async Task RunAsync_BargeInDisabled_ShortNoisePartial_DoesNotCancel()
    {
        // Barge-in off and a below-threshold partial: the single turn completes normally.
        var stt = new ScriptedNoGateStt(new[]
        {
            new SpeechToTextResponseUpdate("hi") { Kind = SpeechToTextResponseUpdateKind.TextUpdating },
            new SpeechToTextResponseUpdate("hello") { Kind = SpeechToTextResponseUpdateKind.TextUpdated },
        });
        var chat = new QuickChat("Sure.");
        var tts = new EchoTts();

        var agent = new VoiceAgent(stt, chat, tts, new VoiceAgentOptions { EnableBargeIn = false });
        using var mic = new MemoryStream(new byte[] { 0 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var updates = new List<VoiceAgentUpdate>();
        await foreach (var update in agent.RunAsync(mic, timeout.Token))
        {
            updates.Add(update);
        }

        Assert.DoesNotContain(updates, u => u.Kind == VoiceAgentUpdateKind.Cancelled);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.TurnComplete);
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

    private sealed class ScriptedNoGateStt : ISpeechToTextClient
    {
        private readonly SpeechToTextResponseUpdate[] _updates;
        public ScriptedNoGateStt(SpeechToTextResponseUpdate[] updates) => _updates = updates;

        public Task<SpeechToTextResponse> GetTextAsync(Stream a, SpeechToTextOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream, SpeechToTextOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var u in _updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return u;
                await Task.Yield();
            }
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

    private sealed class QuickChat : IChatClient
    {
        private readonly string _reply;
        public QuickChat(string reply) => _reply = reply;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, _reply);
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

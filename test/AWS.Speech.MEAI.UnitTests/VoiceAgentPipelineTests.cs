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

public class VoiceAgentPipelineTests
{
    private const string SampleText = "hello";

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_SingleTurn_EmitsExpectedSequence()
    {
        // Arrange: STT scripts one partial + one final; chat emits a two-clause reply; TTS echoes bytes.
        var stt = new ScriptedStt(new[]
        {
            new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionOpen },
            new SpeechToTextResponseUpdate("hel") { Kind = SpeechToTextResponseUpdateKind.TextUpdating },
            new SpeechToTextResponseUpdate("hello there") { Kind = SpeechToTextResponseUpdateKind.TextUpdated },
            new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionClose },
        });

        // Two clauses split by the comma: "Hi, my friend." then " How can I help?"
        var chat = new ScriptedChat("Hi, my friend. How can I help?");
        var tts = new EchoTts();

        var agent = new VoiceAgent(stt, chat, tts);
        using var mic = new MemoryStream(new byte[] { 1, 2, 3 });

        // Act
        var updates = new List<VoiceAgentUpdate>();
        await foreach (var update in agent.RunAsync(mic))
        {
            updates.Add(update);
        }

        // Assert: transcript, then turn start, at least one text delta and audio chunk, then turn complete.
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.UserTranscriptPartial && u.Text == "hel");
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.UserTranscriptFinal && u.Text == "hello there" && u.IsFinal);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.TurnStarted);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.AssistantText);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.AssistantAudio && u.Audio.HasValue);
        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.TurnComplete);

        // Ordering: turn start comes after final, and completes after all its text/audio.
        int finalIdx = updates.FindIndex(u => u.Kind == VoiceAgentUpdateKind.UserTranscriptFinal);
        int startIdx = updates.FindIndex(u => u.Kind == VoiceAgentUpdateKind.TurnStarted);
        int completeIdx = updates.FindIndex(u => u.Kind == VoiceAgentUpdateKind.TurnComplete);
        Assert.True(finalIdx < startIdx, "Turn started should come after user final.");
        Assert.True(startIdx < completeIdx, "Turn should complete after it starts.");

        // Clause chunking must have produced at least two audio chunks (one per clause).
        int audioChunks = updates.Count(u => u.Kind == VoiceAgentUpdateKind.AssistantAudio);
        Assert.True(audioChunks >= 2, $"Expected at least two audio chunks from clause chunking, got {audioChunks}.");

        // Reassembling the assistant deltas reproduces the full reply.
        var fullText = string.Concat(updates.Where(u => u.Kind == VoiceAgentUpdateKind.AssistantText).Select(u => u.Text));
        Assert.Equal("Hi, my friend. How can I help?", fullText);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_WhitespaceFinal_DoesNotStartTurn()
    {
        var stt = new ScriptedStt(new[]
        {
            new SpeechToTextResponseUpdate("   ") { Kind = SpeechToTextResponseUpdateKind.TextUpdated },
        });
        var chat = new FailingChat();
        var tts = new EchoTts();
        var agent = new VoiceAgent(stt, chat, tts);
        using var mic = new MemoryStream(new byte[] { 0 });

        var updates = new List<VoiceAgentUpdate>();
        await foreach (var update in agent.RunAsync(mic))
        {
            updates.Add(update);
        }

        Assert.Contains(updates, u => u.Kind == VoiceAgentUpdateKind.UserTranscriptFinal);
        Assert.DoesNotContain(updates, u => u.Kind == VoiceAgentUpdateKind.TurnStarted);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_InstructionsSeedHistoryAsSystemMessage()
    {
        var stt = new ScriptedStt(new[]
        {
            new SpeechToTextResponseUpdate(SampleText) { Kind = SpeechToTextResponseUpdateKind.TextUpdated },
        });
        var chat = new ScriptedChat("ok.");
        var tts = new EchoTts();
        var agent = new VoiceAgent(stt, chat, tts,
            new VoiceAgentOptions { Instructions = "You are a clinic intake assistant." });

        using var mic = new MemoryStream(new byte[] { 0 });
        await foreach (var _ in agent.RunAsync(mic)) { }

        Assert.NotEmpty(chat.LastHistory);
        Assert.Equal(ChatRole.System, chat.LastHistory[0].Role);
        Assert.Equal("You are a clinic intake assistant.", chat.LastHistory[0].Text);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void Ctor_NullClient_Throws()
    {
        var stt = new ScriptedStt(Array.Empty<SpeechToTextResponseUpdate>());
        var chat = new ScriptedChat(string.Empty);
        var tts = new EchoTts();

        Assert.Throws<ArgumentNullException>(() => new VoiceAgent(null!, chat, tts));
        Assert.Throws<ArgumentNullException>(() => new VoiceAgent(stt, null!, tts));
        Assert.Throws<ArgumentNullException>(() => new VoiceAgent(stt, chat, null!));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task DisposeAsync_ThenRunAsync_ThrowsObjectDisposed()
    {
        var agent = new VoiceAgent(new ScriptedStt(Array.Empty<SpeechToTextResponseUpdate>()),
            new ScriptedChat(string.Empty), new EchoTts());
        await agent.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => agent.RunAsync(new MemoryStream(new byte[] { 0 })));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void GetService_ReturnsUnderlyingClients()
    {
        var stt = new ScriptedStt(Array.Empty<SpeechToTextResponseUpdate>());
        var chat = new ScriptedChat(string.Empty);
        var tts = new EchoTts();
        var agent = new VoiceAgent(stt, chat, tts);

        Assert.Same(stt, agent.GetService(typeof(ISpeechToTextClient)));
        Assert.Same(chat, agent.GetService(typeof(IChatClient)));
        Assert.Same(tts, agent.GetService(typeof(ITextToSpeechClient)));
        Assert.Same(agent, agent.GetService(typeof(VoiceAgent)));
        Assert.Null(agent.GetService(typeof(string)));
    }

    // ---- test doubles ----

    private sealed class ScriptedStt : ISpeechToTextClient
    {
        private readonly SpeechToTextResponseUpdate[] _updates;
        public ScriptedStt(SpeechToTextResponseUpdate[] updates) => _updates = updates;

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

    private sealed class ScriptedChat : IChatClient
    {
        private readonly string _reply;
        public int CallCount { get; private set; }
        public List<ChatMessage> LastHistory { get; private set; } = new();

        public ScriptedChat(string reply) => _reply = reply;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CallCount++;
            LastHistory = messages.ToList();

            // Stream one character at a time to exercise the clause-chunker boundary detection.
            foreach (var ch in _reply)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, ch.ToString());
                await Task.Yield();
            }
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class FailingChat : IChatClient
    {
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Chat should not be invoked for whitespace-only user turns.");
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class EchoTts : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(string text, TextToSpeechOptions? options, CancellationToken cancellationToken) =>
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

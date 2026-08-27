// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI;

public class NovaSonicRunnerTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task RunAsync_PumpsAudioThenMapsServerMessages()
    {
        var audioBytes = new byte[] { 5, 6, 7 };
        var scripted = new RealtimeServerMessage[]
        {
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated) { ResponseId = "r1" },
            new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta) { Text = "Hi", ResponseId = "r1" },
            new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
            {
                Audio = Convert.ToBase64String(audioBytes), ResponseId = "r1",
            },
            new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                Status = RealtimeResponseStatus.Completed, ResponseId = "r1",
            },
        };

        var session = new FakeSession(scripted);
        var client = new FakeRealtimeClient(session);
        var options = new VoiceAgentOptions { ModelId = "amazon.nova-sonic-v1:0" };
        using var mic = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var updates = new List<VoiceAgentUpdate>();
        await foreach (var update in NovaSonicRunner.RunAsync(client, options, mic, CancellationToken.None))
        {
            updates.Add(update);
        }

        Assert.Collection(updates,
            u => Assert.Equal(VoiceAgentUpdateKind.TurnStarted, u.Kind),
            u => { Assert.Equal(VoiceAgentUpdateKind.AssistantText, u.Kind); Assert.Equal("Hi", u.Text); },
            u => { Assert.Equal(VoiceAgentUpdateKind.AssistantAudio, u.Kind); Assert.Equal(audioBytes, u.Audio!.Value.ToArray()); },
            u => Assert.Equal(VoiceAgentUpdateKind.TurnComplete, u.Kind));

        Assert.NotEmpty(session.AppendedAudio);
        Assert.True(session.Committed, "The runner should commit input audio at end of stream.");
        Assert.True(session.Disposed, "The runner should dispose the session.");

        var sessionOptions = client.LastOptions;
        Assert.NotNull(sessionOptions);
        Assert.Equal("amazon.nova-sonic-v1:0", sessionOptions!.Model);
        Assert.Equal("audio/lpcm", sessionOptions.InputAudioFormat!.MediaType);
    }

    private sealed class FakeRealtimeClient : IRealtimeClient
    {
        private readonly FakeSession _session;
        public FakeRealtimeClient(FakeSession session) => _session = session;

        public RealtimeSessionOptions? LastOptions { get; private set; }

        public Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            _session.Options = options ?? new RealtimeSessionOptions();
            return Task.FromResult<IRealtimeClientSession>(_session);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeSession : IRealtimeClientSession
    {
        private readonly RealtimeServerMessage[] _messages;
        private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeSession(RealtimeServerMessage[] messages) => _messages = messages;

        public List<byte[]> AppendedAudio { get; } = new();
        public bool Committed { get; private set; }
        public bool Disposed { get; private set; }
        public RealtimeSessionOptions Options { get; set; } = new();

        public Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken)
        {
            switch (message)
            {
                case InputAudioBufferAppendRealtimeClientMessage append:
                    AppendedAudio.Add(append.Content.Data.ToArray());
                    break;
                case InputAudioBufferCommitRealtimeClientMessage:
                    Committed = true;
                    _committed.TrySetResult();
                    break;
            }
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Deterministic ordering: don't produce server messages until the runner has pumped and
            // committed the input audio.
            await _committed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            foreach (var message in _messages)
            {
                yield return message;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
#endif

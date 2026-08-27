// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using AWS.Bedrock.MEAI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// Exposes a <see cref="VoiceAgent"/> pipeline through MEAI's <see cref="IRealtimeClient"/> contract, so
/// code written against the Nova Sonic realtime surface runs against the pipeline without call-site
/// changes. The message types match; timing and cadence differ (see the design notes).
/// </summary>
internal sealed class VoiceAgentRealtimeClient : IRealtimeClient
{
    private readonly VoiceAgent _agent;
    private readonly string? _defaultModelId;

    public VoiceAgentRealtimeClient(VoiceAgent agent, string? defaultModelId)
    {
        _agent = agent;
        _defaultModelId = defaultModelId;
    }

    public Task<IRealtimeClientSession> CreateSessionAsync(RealtimeSessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IRealtimeClientSession session = new VoiceAgentRealtimeSession(_agent, options ?? new RealtimeSessionOptions());
        return Task.FromResult(session);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
        if (serviceKey is null && serviceType.IsInstanceOfType(this)) return this;
        return _agent.GetService(serviceType, serviceKey);
    }

    // IRealtimeClient derives from IDisposable. This adapter does not own the VoiceAgent, so it disposes nothing.
    public void Dispose() { }

    private sealed class VoiceAgentRealtimeSession : IRealtimeClientSession, IAsyncDisposable
    {
        private readonly VoiceAgent _agent;
        private readonly Pipe _inputAudio = new();
        private int _enumeration;
        private int _writerCompleted;

        public VoiceAgentRealtimeSession(VoiceAgent agent, RealtimeSessionOptions options)
        {
            _agent = agent;
            Options = options;
        }

        public RealtimeSessionOptions Options { get; }

        public async Task SendAsync(RealtimeClientMessage message, CancellationToken cancellationToken)
        {
            switch (message)
            {
                case InputAudioBufferAppendRealtimeClientMessage append:
                    await _inputAudio.Writer.WriteAsync(append.Content.Data, cancellationToken).ConfigureAwait(false);
                    break;

                // The pipeline runs its own endpointing off the continuous audio, so a commit is a no-op.
                // Other client messages (session updates, response requests) are pipeline-driven and ignored.
                case InputAudioBufferCommitRealtimeClientMessage:
                default:
                    break;
            }
        }

        public async IAsyncEnumerable<RealtimeServerMessage> GetStreamingResponseAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!RealtimeAudioProtocol.TryBeginExclusiveEnumeration(ref _enumeration))
            {
                throw new InvalidOperationException(
                    "Only one active streaming enumeration is allowed at a time for a realtime session.");
            }

            try
            {
                var microphone = _inputAudio.Reader.AsStream();
                await foreach (var update in _agent.RunAsync(microphone, cancellationToken).ConfigureAwait(false))
                {
                    if (RealtimeMessageMapper.ToServerMessage(update) is { } message)
                    {
                        yield return message;
                    }
                }
            }
            finally
            {
                RealtimeAudioProtocol.EndExclusiveEnumeration(ref _enumeration);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
            if (serviceKey is null && serviceType.IsInstanceOfType(this)) return this;
            return _agent.GetService(serviceType, serviceKey);
        }

        public ValueTask DisposeAsync()
        {
            // Completing the writer signals end-of-audio, which lets the pipeline's speech-to-text leg
            // drain and the enumeration finish.
            if (Interlocked.Exchange(ref _writerCompleted, 1) == 0)
            {
                _inputAudio.Writer.Complete();
            }
            return default;
        }
    }
}
#endif

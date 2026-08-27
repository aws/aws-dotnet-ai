// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// Drives the <see cref="VoiceAgentBackend.NovaSonic"/> backend: opens a realtime session on the
/// supplied <see cref="IRealtimeClient"/> (Amazon Bedrock Nova Sonic), streams microphone PCM into it
/// as input-audio messages, and maps the session's realtime server messages back to
/// <see cref="VoiceAgentUpdate"/>s so the caller sees the same stream as the pipeline backend.
/// </summary>
internal static class NovaSonicRunner
{
    private const int AudioChunkBytes = 8192;

    public static async IAsyncEnumerable<VoiceAgentUpdate> RunAsync(
        IRealtimeClient client, VoiceAgentOptions options, Stream microphonePcm,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionOptions = new RealtimeSessionOptions
        {
            Model = options.ModelId,
            Instructions = options.Instructions,
            Voice = options.Voice.Value,
            InputAudioFormat = new RealtimeAudioFormat("audio/lpcm", options.InputSampleRateHertz),
            OutputAudioFormat = new RealtimeAudioFormat("audio/lpcm", options.OutputSampleRateHertz),
        };

        var session = await client.CreateSessionAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var pump = Task.Run(() => PumpAudioAsync(session, microphonePcm, pumpCts.Token), pumpCts.Token);
        try
        {
            await foreach (var message in session.GetStreamingResponseAsync(cancellationToken).ConfigureAwait(false))
            {
                if (RealtimeMessageMapper.ToVoiceAgentUpdate(message) is { } update)
                {
                    yield return update;
                }
            }
        }
        finally
        {
            pumpCts.Cancel();
            try { await pump.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on teardown */ }
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task PumpAudioAsync(IRealtimeClientSession session, Stream microphonePcm, CancellationToken token)
    {
        var buffer = new byte[AudioChunkBytes];
        int read;
        while ((read = await microphonePcm.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
        {
            var chunk = new byte[read];
            Array.Copy(buffer, 0, chunk, 0, read);
            var message = new InputAudioBufferAppendRealtimeClientMessage(new DataContent(chunk, "audio/lpcm"));
            await session.SendAsync(message, token).ConfigureAwait(false);
        }

        // End of the caller's audio: commit so the model can finalize the current utterance.
        await session.SendAsync(new InputAudioBufferCommitRealtimeClientMessage(), token).ConfigureAwait(false);
    }
}
#endif

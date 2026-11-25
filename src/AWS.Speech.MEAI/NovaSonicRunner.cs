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
            // Nova Sonic voice IDs are lowercase (e.g. "matthew"); the Polly VoiceId wire value is PascalCase
            // (e.g. "Matthew"), so normalize it or the service rejects an otherwise valid default.
            Voice = options.Voice.Value.ToLowerInvariant(),
            InputAudioFormat = new RealtimeAudioFormat("audio/lpcm", options.InputSampleRateHertz),
            OutputAudioFormat = new RealtimeAudioFormat("audio/lpcm", options.OutputSampleRateHertz),
            // Forward the configured tools so the Nova backend advertises/invokes the same tools as the
            // pipeline backend (which passes them through ChatOptions.Tools).
            Tools = options.Tools?.ToArray(),
        };

        var session = await client.CreateSessionAsync(sessionOptions, cancellationToken).ConfigureAwait(false);
        try
        {
            // A single cancellation source ties the audio pump to the response enumeration: if the pump
            // faults (mic read or SendAsync), it cancels this source so the read loop below stops waiting for
            // input that will never arrive instead of hanging indefinitely.
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var pump = Task.Run(async () =>
            {
                try
                {
                    await PumpAudioAsync(session, microphonePcm, sessionCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the response stream ends first and cancels the pump on teardown.
                }
                catch
                {
                    sessionCts.Cancel();   // a real pump fault tears down the read loop, then is rethrown below
                    throw;
                }
            }, sessionCts.Token);

            try
            {
                await foreach (var message in session.GetStreamingResponseAsync(sessionCts.Token).ConfigureAwait(false))
                {
                    if (RealtimeMessageMapper.ToVoiceAgentUpdate(message) is { } update)
                    {
                        yield return update;
                    }
                }
            }
            finally
            {
                sessionCts.Cancel();
                // Surfaces a non-cancellation pump fault (e.g. a microphone read error); a teardown
                // cancellation is swallowed as expected.
                try { await pump.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected on teardown */ }
            }
        }
        finally
        {
            // Always dispose the session, even if the pump faulted before the read loop was entered.
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

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon.TranscribeStreaming;
using Amazon.TranscribeStreaming.Model;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// An <see cref="ISpeechToTextClient"/> backed by Amazon Transcribe streaming. Feeds PCM audio through
/// the bidirectional HTTP/2 stream and surfaces partial and final transcripts as MEAI updates.
/// </summary>
/// <remarks>
/// Amazon Transcribe's only real-time API, <c>StartStreamTranscription</c>, is a bidirectional HTTP/2
/// event stream, so this client is available on net8.0 and later only. It expects 16-bit signed
/// little-endian mono PCM. The caller's input <see cref="Stream"/> is never disposed by this client.
/// </remarks>
internal sealed class TranscribeSpeechToTextClient : ISpeechToTextClient
{
    private const int AudioChunkBytes = 8192;

    private readonly IAmazonTranscribeStreaming _client;
    private readonly string _defaultLanguage;
    private readonly int _defaultSampleRateHertz;

    public TranscribeSpeechToTextClient(
        IAmazonTranscribeStreaming client,
        string? defaultLanguage = "en-US",
        int defaultSampleRateHertz = 16000)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _defaultLanguage = string.IsNullOrEmpty(defaultLanguage) ? "en-US" : defaultLanguage!;
        _defaultSampleRateHertz = defaultSampleRateHertz;
    }

    /// <inheritdoc/>
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream, SpeechToTextOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (audioSpeechStream is null) throw new ArgumentNullException(nameof(audioSpeechStream));

        var transcript = new StringBuilder();
        await foreach (var update in GetStreamingTextAsync(audioSpeechStream, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (update.Kind == SpeechToTextResponseUpdateKind.TextUpdated && !string.IsNullOrEmpty(update.Text))
            {
                if (transcript.Length > 0) transcript.Append(' ');
                transcript.Append(update.Text);
            }
        }

        return new SpeechToTextResponse(transcript.ToString());
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream, SpeechToTextOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (audioSpeechStream is null) throw new ArgumentNullException(nameof(audioSpeechStream));

        var request = BuildRequest(audioSpeechStream, options, cancellationToken);

        using var response = await _client.StartStreamTranscriptionAsync(request, cancellationToken)
            .ConfigureAwait(false);

        yield return new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionOpen };

        var stream = response.TranscriptResultStream;
        if (stream is null)
        {
            yield return new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionClose };
            yield break;
        }

        await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool moved;
            SpeechToTextResponseUpdate? failure = null;
            try
            {
                moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Resilience (reconnect/resume) is a non-goal; surface the failure and tear down.
                failure = new SpeechToTextResponseUpdate(ex.Message)
                {
                    Kind = SpeechToTextResponseUpdateKind.Error,
                    RawRepresentation = ex,
                };
                moved = false;
            }

            if (failure is not null)
            {
                yield return failure;
                break;
            }

            if (!moved) break;

            if (enumerator.Current is TranscriptEvent transcriptEvent)
            {
                foreach (var update in TranslateTranscript(transcriptEvent))
                {
                    yield return update;
                }
            }
        }

        yield return new SpeechToTextResponseUpdate { Kind = SpeechToTextResponseUpdateKind.SessionClose };
    }

    /// <inheritdoc/>
    public object? GetService(System.Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
        if (serviceKey is not null) return null;
        if (serviceType == typeof(IAmazonTranscribeStreaming)) return _client;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    void IDisposable.Dispose()
    {
        // The Amazon Transcribe client is owned by the caller (or the DI container), so it is not disposed here.
    }

    internal static IEnumerable<SpeechToTextResponseUpdate> TranslateTranscript(TranscriptEvent transcriptEvent)
    {
        var results = transcriptEvent.Transcript?.Results;
        if (results is null) yield break;

        foreach (var result in results)
        {
            var text = result.Alternatives is { Count: > 0 } alternatives ? alternatives[0].Transcript : null;
            if (string.IsNullOrEmpty(text)) continue;

            var isPartial = result.IsPartial ?? false;
            yield return new SpeechToTextResponseUpdate(text)
            {
                Kind = isPartial
                    ? SpeechToTextResponseUpdateKind.TextUpdating
                    : SpeechToTextResponseUpdateKind.TextUpdated,
                StartTime = result.StartTime is { } start ? TimeSpan.FromSeconds(start) : null,
                EndTime = result.EndTime is { } end ? TimeSpan.FromSeconds(end) : null,
                RawRepresentation = result,
            };
        }
    }

    private StartStreamTranscriptionRequest BuildRequest(
        Stream audioSpeechStream, SpeechToTextOptions? options, CancellationToken cancellationToken)
    {
        // Honor a caller-supplied request via RawRepresentationFactory, then fill in defaults it left unset.
        var request = options?.RawRepresentationFactory?.Invoke(this) as StartStreamTranscriptionRequest
            ?? new StartStreamTranscriptionRequest();

        var language = !string.IsNullOrEmpty(options?.SpeechLanguage) ? options!.SpeechLanguage : _defaultLanguage;
        request.LanguageCode ??= new LanguageCode(language);
        request.MediaEncoding ??= MediaEncoding.Pcm;
        request.MediaSampleRateHertz ??= options?.SpeechSampleRate ?? _defaultSampleRateHertz;

        // Pull PCM off the caller's stream and hand it to Transcribe one chunk at a time; null signals end of audio.
        request.AudioStreamPublisher = async () =>
        {
            var buffer = new byte[AudioChunkBytes];
            int read = await audioSpeechStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                .ConfigureAwait(false);
            if (read <= 0) return null!;   // null signals end of audio to Amazon Transcribe
            return new AudioEvent { AudioChunk = new MemoryStream(buffer, 0, read) };
        };

        return request;
    }
}
#endif

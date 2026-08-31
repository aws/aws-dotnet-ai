// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Polly;
using Amazon.Polly.Model;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// An <see cref="ITextToSpeechClient"/> backed by Amazon Polly. Synthesizes text into audio using
/// <see cref="IAmazonPolly.SynthesizeSpeechAsync"/> and delivers it as a <see cref="DataContent"/>.
/// </summary>
/// <remarks>
/// Defaults to 16 kHz, 16-bit signed little-endian mono PCM (<c>audio/lpcm</c>). Amazon Polly's PCM
/// output supports only 8000 Hz and 16000 Hz, so a PCM request at any other sample rate is rejected.
/// This client runs on every target framework the package supports.
/// </remarks>
internal sealed class PollyTextToSpeechClient : ITextToSpeechClient
{
    /// <summary>MEAI media type for linear PCM audio.</summary>
    internal const string LpcmMediaType = "audio/lpcm";

    private readonly IAmazonPolly _polly;
    private readonly VoiceId _defaultVoice;
    private readonly Engine _defaultEngine;
    private readonly int _defaultSampleRateHertz;

    public PollyTextToSpeechClient(
        IAmazonPolly polly,
        VoiceId? defaultVoice = null,
        Engine? defaultEngine = null,
        int defaultSampleRateHertz = 16000)
    {
        _polly = polly ?? throw new ArgumentNullException(nameof(polly));
        _defaultVoice = defaultVoice ?? VoiceId.Matthew;
        _defaultEngine = defaultEngine ?? Engine.Neural;
        _defaultSampleRateHertz = defaultSampleRateHertz;
    }

    /// <inheritdoc/>
    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text, TextToSpeechOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var request = BuildRequest(text, options);
        using var response = await _polly.SynthesizeSpeechAsync(request, cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        if (response.AudioStream is not null)
        {
            await response.AudioStream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        }

        var mediaType = MediaTypeFor(request.OutputFormat);
        var content = new DataContent(buffer.ToArray(), mediaType);
        return new TextToSpeechResponse(new List<AIContent> { content });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text, TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var request = BuildRequest(text, options);
        var mediaType = MediaTypeFor(request.OutputFormat);

        using var response = await _polly.SynthesizeSpeechAsync(request, cancellationToken).ConfigureAwait(false);

        yield return new TextToSpeechResponseUpdate { Kind = TextToSpeechResponseUpdateKind.SessionOpen };

        if (response.AudioStream is not null)
        {
            var buffer = new byte[16384];
            int read;
            while ((read = await response.AudioStream
                .ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                var chunk = new byte[read];
                Array.Copy(buffer, 0, chunk, 0, read);
                yield return new TextToSpeechResponseUpdate(new List<AIContent> { new DataContent(chunk, mediaType) })
                {
                    Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
                };
            }
        }

        yield return new TextToSpeechResponseUpdate { Kind = TextToSpeechResponseUpdateKind.SessionClose };
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
        if (serviceKey is not null) return null;
        if (serviceType == typeof(IAmazonPolly)) return _polly;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    void IDisposable.Dispose()
    {
        // The Amazon Polly client is owned by the caller (or the DI container), so it is not disposed here.
    }

    private SynthesizeSpeechRequest BuildRequest(string text, TextToSpeechOptions? options)
    {
        // Honor a caller-supplied request via RawRepresentationFactory, then fill in defaults it left unset.
        var request = options?.RawRepresentationFactory?.Invoke(this) as SynthesizeSpeechRequest
            ?? new SynthesizeSpeechRequest();

        request.Text = text;
        // Honor the MEAI-desired output format when the caller did not pin an OutputFormat via the raw request.
        request.OutputFormat ??= MapAudioFormat(options?.AudioFormat) ?? OutputFormat.Pcm;
        request.Engine ??= _defaultEngine;
        request.VoiceId ??= ResolveVoice(options);

        if (!string.IsNullOrEmpty(options?.Language))
        {
            request.LanguageCode ??= new LanguageCode(options!.Language);
        }

        if (string.IsNullOrEmpty(request.SampleRate))
        {
            request.SampleRate = _defaultSampleRateHertz.ToString(CultureInfo.InvariantCulture);
        }

        ValidateSampleRate(request);
        return request;
    }

    // Translate the MEAI TextToSpeechOptions.AudioFormat (a media type such as "audio/mpeg" or a
    // provider-specific name such as "mp3") into a Polly OutputFormat. Returns null when the caller
    // left AudioFormat unset (so BuildRequest falls back to PCM); throws for unsupported values.
    private static OutputFormat? MapAudioFormat(string? audioFormat)
    {
        if (string.IsNullOrWhiteSpace(audioFormat)) return null;

        switch (audioFormat!.Trim().ToLowerInvariant())
        {
            case "audio/lpcm":
            case "audio/pcm":
            case "pcm":
                return OutputFormat.Pcm;
            case "audio/mpeg":
            case "audio/mp3":
            case "mp3":
                return OutputFormat.Mp3;
            case "ogg_vorbis":
            case "ogg-vorbis":
            case "vorbis":
                return OutputFormat.Ogg_vorbis;
            case "audio/ogg":
            case "ogg":
            case "ogg_opus":
            case "ogg-opus":
            case "opus":
                return OutputFormat.Ogg_opus;
            default:
                throw new ArgumentException(
                    $"Unsupported TextToSpeechOptions.AudioFormat '{audioFormat}'. Amazon Polly supports PCM " +
                    $"(audio/lpcm), MP3 (audio/mpeg), and Ogg (audio/ogg). Supply a Polly OutputFormat directly " +
                    $"via TextToSpeechOptions.RawRepresentationFactory for other formats.",
                    nameof(audioFormat));
        }
    }

    private VoiceId ResolveVoice(TextToSpeechOptions? options) =>
        !string.IsNullOrEmpty(options?.VoiceId) ? new VoiceId(options!.VoiceId) : _defaultVoice;

    private static void ValidateSampleRate(SynthesizeSpeechRequest request)
    {
        // Amazon Polly only accepts 8000 Hz and 16000 Hz for PCM output.
        if (IsFormat(request.OutputFormat, OutputFormat.Pcm) &&
            request.SampleRate is not "8000" and not "16000")
        {
            throw new ArgumentException(
                $"Amazon Polly PCM output supports only 8000 Hz or 16000 Hz, but the requested sample rate was " +
                $"'{request.SampleRate}'. Set OutputSampleRateHertz to 8000 or 16000, or request a non-PCM OutputFormat.",
                nameof(request));
        }
    }

    private static string MediaTypeFor(OutputFormat? format)
    {
        if (IsFormat(format, OutputFormat.Pcm)) return LpcmMediaType;
        if (IsFormat(format, OutputFormat.Mp3)) return "audio/mpeg";
        if (IsFormat(format, OutputFormat.Ogg_vorbis) || IsFormat(format, OutputFormat.Ogg_opus)) return "audio/ogg";
        return "application/octet-stream";
    }

    // AWS SDK ConstantClass instances are compared by value, not reference: a caller-supplied
    // request may use a freshly constructed OutputFormat rather than the cached singleton.
    private static bool IsFormat(OutputFormat? format, OutputFormat expected) =>
        string.Equals(format?.Value, expected.Value, StringComparison.OrdinalIgnoreCase);
}

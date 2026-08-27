// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Polly;
using Amazon.Runtime;
using Amazon.TranscribeStreaming;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

/// <summary>
/// Composes an <see cref="ISpeechToTextClient"/>, an <see cref="IChatClient"/>, and an
/// <see cref="ITextToSpeechClient"/> into a full-duplex voice loop that emits one ordered stream of
/// <see cref="VoiceAgentUpdate"/>s.
/// </summary>
/// <remarks>
/// The default backend is <see cref="VoiceAgentBackend.Pipeline"/>: Amazon Transcribe streaming for STT,
/// Amazon Bedrock via <c>AWS.Bedrock.MEAI</c> for reasoning, and Amazon Polly for TTS. Set
/// <see cref="VoiceAgentOptions.Backend"/> to <see cref="VoiceAgentBackend.NovaSonic"/> on
/// <see cref="Create"/> to route the same <c>RunAsync</c> stream through Amazon Bedrock Nova Sonic
/// instead. <see cref="AsIRealtimeClient"/> exposes either backend through MEAI's
/// <see cref="IRealtimeClient"/> contract.
/// </remarks>
[Experimental("MEAI001")]
public sealed class VoiceAgent : IAsyncDisposable
{
    private readonly ISpeechToTextClient? _stt;
    private readonly IChatClient? _chat;
    private readonly ITextToSpeechClient? _tts;
    private readonly IRealtimeClient? _novaClient;
    private readonly VoiceAgentOptions _options;
    private readonly List<IDisposable> _ownedResources;
    private int _disposed;

    /// <summary>Initializes a provider-neutral pipeline <see cref="VoiceAgent"/> around any MEAI clients.</summary>
    /// <exception cref="ArgumentNullException">A client is <see langword="null"/>.</exception>
    public VoiceAgent(ISpeechToTextClient stt, IChatClient chat, ITextToSpeechClient tts, VoiceAgentOptions? options = null)
        : this(
            stt ?? throw new ArgumentNullException(nameof(stt)),
            chat ?? throw new ArgumentNullException(nameof(chat)),
            tts ?? throw new ArgumentNullException(nameof(tts)),
            novaClient: null,
            options ?? new VoiceAgentOptions(),
            ownedResources: null)
    {
    }

    private VoiceAgent(
        ISpeechToTextClient? stt, IChatClient? chat, ITextToSpeechClient? tts, IRealtimeClient? novaClient,
        VoiceAgentOptions options, List<IDisposable>? ownedResources)
    {
        _stt = stt;
        _chat = chat;
        _tts = tts;
        _novaClient = novaClient;
        _options = options;
        _ownedResources = ownedResources ?? new List<IDisposable>();
    }

    /// <summary>Creates a <see cref="VoiceAgent"/> with the default AWS credential and region chains.</summary>
    /// <remarks>
    /// For <see cref="VoiceAgentBackend.Pipeline"/> this constructs an Amazon Transcribe streaming client,
    /// an Amazon Bedrock runtime client adapted to an <see cref="IChatClient"/>, and an Amazon Polly client.
    /// For <see cref="VoiceAgentBackend.NovaSonic"/> it constructs an Amazon Bedrock runtime client adapted
    /// to an <see cref="IRealtimeClient"/>. Both honor <see cref="VoiceAgentOptions.Credentials"/> and
    /// <see cref="VoiceAgentOptions.Region"/>, and a pre-built client on the options wins over the default
    /// AWS client for its leg. The returned agent owns any clients it constructed and disposes them on
    /// <see cref="DisposeAsync"/>.
    /// </remarks>
    public static VoiceAgent Create(Action<VoiceAgentOptions>? configure = null)
    {
        var options = new VoiceAgentOptions();
        configure?.Invoke(options);

        // Fail fast: when no pre-built ChatClient is supplied the factory builds a default Amazon Bedrock
        // client (an IChatClient for the pipeline backend, an IRealtimeClient for Nova Sonic), and Bedrock
        // requires a model to invoke. Validate here, before allocating any AWS clients, so the caller gets a
        // clear error at construction instead of an opaque failure after the loop has started.
        if (options.ChatClient is null && string.IsNullOrEmpty(options.ModelId))
        {
            throw new ArgumentException(
                "A ModelId is required when no ChatClient is supplied, because the default Amazon Bedrock " +
                "client needs a model to invoke. Set VoiceAgentOptions.ModelId or supply VoiceAgentOptions.ChatClient.",
                nameof(configure));
        }

        var owned = new List<IDisposable>();

        if (options.Backend == VoiceAgentBackend.NovaSonic)
        {
            var bedrock = CreateBedrockClient(options.Credentials, options.Region);
            owned.Add(bedrock);
            var nova = bedrock.AsIRealtimeClient(options.ModelId);
            if (nova is IDisposable disposableNova) owned.Add(disposableNova);
            return new VoiceAgent(stt: null, chat: null, tts: null, nova, options, owned);
        }

        var stt = options.SpeechToTextClient;
        if (stt is null)
        {
            var transcribe = CreateTranscribeClient(options.Credentials, options.Region);
            owned.Add(transcribe);
            stt = transcribe.AsISpeechToTextClient(options.Language, options.InputSampleRateHertz);
        }

        var chat = options.ChatClient;
        if (chat is null)
        {
            var bedrock = CreateBedrockClient(options.Credentials, options.Region);
            owned.Add(bedrock);
            chat = bedrock.AsIChatClient(options.ModelId);
        }

        var tts = options.TextToSpeechClient;
        if (tts is null)
        {
            var polly = CreatePollyClient(options.Credentials, options.Region);
            owned.Add(polly);
            tts = polly.AsITextToSpeechClient(options.Voice, Engine.Neural, options.OutputSampleRateHertz);
        }

        return new VoiceAgent(stt, chat, tts, novaClient: null, options, owned);
    }

    /// <summary>Runs the voice loop over the caller's microphone PCM stream.</summary>
    /// <param name="microphonePcm">
    /// Input audio at <see cref="VoiceAgentOptions.InputSampleRateHertz"/>, 16-bit signed little-endian
    /// mono PCM. The agent never disposes this stream.
    /// </param>
    /// <param name="cancellationToken">Stops the loop; the returned enumerable then completes.</param>
    /// <returns>One ordered stream of <see cref="VoiceAgentUpdate"/>s.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="microphonePcm"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The agent has been disposed.</exception>
    /// <exception cref="NotSupportedException">
    /// The agent is configured for <see cref="VoiceAgentBackend.NovaSonic"/> but was built with the
    /// provider-neutral constructor, which is pipeline-only. Use <see cref="Create"/> for Nova Sonic.
    /// </exception>
    public IAsyncEnumerable<VoiceAgentUpdate> RunAsync(Stream microphonePcm, CancellationToken cancellationToken = default)
    {
        if (microphonePcm is null) throw new ArgumentNullException(nameof(microphonePcm));
        ThrowIfDisposed();

        if (_options.Backend == VoiceAgentBackend.NovaSonic)
        {
            if (_novaClient is null)
            {
                throw new NotSupportedException(
                    "The NovaSonic backend requires VoiceAgent.Create; the provider-neutral constructor is pipeline-only.");
            }
            return NovaSonicRunner.RunAsync(_novaClient, _options, microphonePcm, cancellationToken);
        }

        return VoiceAgentPipeline.RunAsync(_stt!, _chat!, _tts!, _options, microphonePcm, cancellationToken);
    }

    /// <summary>Exposes this agent through MEAI's <see cref="IRealtimeClient"/> contract.</summary>
    /// <param name="defaultModelId">An optional default model ID recorded on the adapter.</param>
    /// <returns>An <see cref="IRealtimeClient"/> whose sessions drive this agent's loop.</returns>
    /// <exception cref="ObjectDisposedException">The agent has been disposed.</exception>
    public IRealtimeClient AsIRealtimeClient(string? defaultModelId = null)
    {
        ThrowIfDisposed();
        return new VoiceAgentRealtimeClient(this, defaultModelId);
    }

    /// <summary>Returns the underlying MEAI client for the requested service type, or <see langword="null"/>.</summary>
    public object? GetService(System.Type serviceType, object? serviceKey = null)
    {
        if (serviceType is null) throw new ArgumentNullException(nameof(serviceType));
        if (serviceKey is not null) return null;

        if (serviceType == typeof(ISpeechToTextClient)) return _stt;
        if (serviceType == typeof(IChatClient)) return _chat;
        if (serviceType == typeof(ITextToSpeechClient)) return _tts;
        if (serviceType == typeof(IRealtimeClient)) return _novaClient;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return default;

        Exception? first = null;
        foreach (var resource in _ownedResources)
        {
            try { resource.Dispose(); }
            catch (Exception ex) { first ??= ex; }
        }
        _ownedResources.Clear();

        if (first is not null)
        {
            ExceptionDispatchInfo.Capture(first).Throw();
        }
        return default;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(VoiceAgent));
        }
    }

    private static AmazonTranscribeStreamingClient CreateTranscribeClient(AWSCredentials? credentials, RegionEndpoint? region) =>
        (credentials, region) switch
        {
            (null, null) => new AmazonTranscribeStreamingClient(),
            (null, _) => new AmazonTranscribeStreamingClient(region),
            (_, null) => new AmazonTranscribeStreamingClient(credentials),
            _ => new AmazonTranscribeStreamingClient(credentials, region),
        };

    private static AmazonBedrockRuntimeClient CreateBedrockClient(AWSCredentials? credentials, RegionEndpoint? region) =>
        (credentials, region) switch
        {
            (null, null) => new AmazonBedrockRuntimeClient(),
            (null, _) => new AmazonBedrockRuntimeClient(region),
            (_, null) => new AmazonBedrockRuntimeClient(credentials),
            _ => new AmazonBedrockRuntimeClient(credentials, region),
        };

    private static AmazonPollyClient CreatePollyClient(AWSCredentials? credentials, RegionEndpoint? region) =>
        (credentials, region) switch
        {
            (null, null) => new AmazonPollyClient(),
            (null, _) => new AmazonPollyClient(region),
            (_, null) => new AmazonPollyClient(credentials),
            _ => new AmazonPollyClient(credentials, region),
        };
}
#endif

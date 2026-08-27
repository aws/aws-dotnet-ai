// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon.BedrockRuntime;
using Amazon.Polly;
using Amazon.TranscribeStreaming;
using AWS.Speech.MEAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Dependency-injection extensions for registering a <see cref="VoiceAgent"/>.</summary>
[Experimental("MEAI001")]
public static class VoiceAgentServiceCollectionExtensions
{
    /// <summary>Registers a <see cref="VoiceAgent"/> and the MEAI speech clients it composes.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for <see cref="VoiceAgentOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// Every registration uses <c>TryAdd</c>, so a caller who has already registered an
    /// <see cref="IAmazonPolly"/>, <see cref="IAmazonTranscribeStreaming"/>, <see cref="IAmazonBedrockRuntime"/>,
    /// <see cref="ISpeechToTextClient"/>, <see cref="IChatClient"/>, or <see cref="ITextToSpeechClient"/> keeps
    /// their own registration. A pre-built client on <see cref="VoiceAgentOptions"/> wins over the AWS client
    /// for that leg. The three AWS clients come from <c>AddAWSService</c>'s default option/credential resolution;
    /// <see cref="VoiceAgentOptions.Region"/> and <see cref="VoiceAgentOptions.Credentials"/> are honored only by
    /// <see cref="VoiceAgent.Create"/>, not by this DI path, where AWS options are configured through the container.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddVoiceAgent(this IServiceCollection services, Action<VoiceAgentOptions>? configure = null)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        var options = new VoiceAgentOptions();
        configure?.Invoke(options);

        RegisterSpeechToText(services, options);
        RegisterChat(services, options);
        RegisterTextToSpeech(services, options);

        services.TryAddSingleton(sp => new VoiceAgent(
            sp.GetRequiredService<ISpeechToTextClient>(),
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<ITextToSpeechClient>(),
            options));

        return services;
    }

    private static void RegisterSpeechToText(IServiceCollection services, VoiceAgentOptions options)
    {
        if (options.SpeechToTextClient is { } stt)
        {
            services.TryAddSingleton(stt);
            return;
        }

        services.TryAddAWSService<IAmazonTranscribeStreaming>();
        services.TryAddSingleton<ISpeechToTextClient>(sp =>
            sp.GetRequiredService<IAmazonTranscribeStreaming>()
                .AsISpeechToTextClient(options.Language, options.InputSampleRateHertz));
    }

    private static void RegisterChat(IServiceCollection services, VoiceAgentOptions options)
    {
        if (options.ChatClient is { } chat)
        {
            services.TryAddSingleton(chat);
            return;
        }

        services.TryAddAWSService<IAmazonBedrockRuntime>();
        services.TryAddSingleton<IChatClient>(sp =>
            sp.GetRequiredService<IAmazonBedrockRuntime>().AsIChatClient(options.ModelId));
    }

    private static void RegisterTextToSpeech(IServiceCollection services, VoiceAgentOptions options)
    {
        if (options.TextToSpeechClient is { } tts)
        {
            services.TryAddSingleton(tts);
            return;
        }

        services.TryAddAWSService<IAmazonPolly>();
        services.TryAddSingleton<ITextToSpeechClient>(sp =>
            sp.GetRequiredService<IAmazonPolly>()
                .AsITextToSpeechClient(options.Voice, Engine.Neural, options.OutputSampleRateHertz));
    }
}
#endif

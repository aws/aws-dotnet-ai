// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Speech.MEAI;
using Microsoft.Extensions.AI;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Amazon.Polly;

/// <summary>Provides extensions for working with <see cref="IAmazonPolly"/> instances.</summary>
public static class AmazonPollyExtensions
{
    /// <summary>Gets an <see cref="ITextToSpeechClient"/> for the specified <see cref="IAmazonPolly"/> instance.</summary>
    /// <param name="client">The Amazon Polly client to represent as an <see cref="ITextToSpeechClient"/>.</param>
    /// <param name="defaultVoice">The default voice to synthesize with. Defaults to <see cref="VoiceId.Matthew"/>.</param>
    /// <param name="defaultEngine">The default engine to synthesize with. Defaults to <see cref="Engine.Neural"/>.</param>
    /// <param name="defaultSampleRateHertz">
    /// The default PCM sample rate in hertz. Amazon Polly PCM output supports only 8000 or 16000; defaults to 16000.
    /// </param>
    /// <returns>An <see cref="ITextToSpeechClient"/> that synthesizes audio with Amazon Polly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Experimental("MEAI001")]
    public static ITextToSpeechClient AsITextToSpeechClient(
        this IAmazonPolly client,
        VoiceId? defaultVoice = null,
        Engine? defaultEngine = null,
        int defaultSampleRateHertz = 16000) =>
        client is not null
            ? new PollyTextToSpeechClient(client, defaultVoice, defaultEngine, defaultSampleRateHertz)
            : throw new ArgumentNullException(nameof(client));
}

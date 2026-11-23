// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.AI;

namespace Amazon.Polly;

/// <summary>
/// Default synthesis settings for an <see cref="ITextToSpeechClient"/> created from an
/// <see cref="IAmazonPolly"/> via <see cref="AmazonPollyExtensions.AsITextToSpeechClient"/>. Carried as
/// an options class so new defaults can be added without a breaking change to the extension signature.
/// </summary>
/// <remarks>
/// These are per-client defaults applied when a value is not set on an individual
/// <see cref="TextToSpeechOptions"/> request. Declared in the <c>Amazon.Polly</c> namespace so it is
/// discoverable alongside the extension method with the same <c>using</c>.
/// </remarks>
public sealed class PollyTextToSpeechClientOptions
{
    /// <summary>The default voice to synthesize with. Defaults to <see cref="VoiceId.Matthew"/>.</summary>
    public VoiceId? DefaultVoice { get; set; }

    /// <summary>The default engine to synthesize with. Defaults to <see cref="Engine.Neural"/>.</summary>
    public Engine? DefaultEngine { get; set; }

    /// <summary>
    /// The default PCM sample rate in hertz. Amazon Polly PCM output supports only 8000 or 16000; defaults to 16000.
    /// </summary>
    public int DefaultSampleRateHertz { get; set; } = 16000;
}

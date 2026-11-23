// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;

namespace Amazon.TranscribeStreaming;

/// <summary>
/// Default transcription settings for an <see cref="ISpeechToTextClient"/> created from an
/// <see cref="IAmazonTranscribeStreaming"/> via
/// <see cref="AmazonTranscribeStreamingExtensions.AsISpeechToTextClient"/>. Carried as an options class
/// so new defaults can be added without a breaking change to the extension signature.
/// </summary>
/// <remarks>
/// These are per-client defaults applied when a value is not set on an individual
/// <see cref="SpeechToTextOptions"/> request. Declared in the <c>Amazon.TranscribeStreaming</c> namespace
/// so it is discoverable alongside the extension method with the same <c>using</c>.
/// </remarks>
public sealed class TranscribeSpeechToTextClientOptions
{
    /// <summary>The default transcription language. Defaults to <c>en-US</c>.</summary>
    public string? DefaultLanguage { get; set; } = "en-US";

    /// <summary>The default PCM sample rate in hertz. Defaults to 16000.</summary>
    public int DefaultSampleRateHertz { get; set; } = 16000;
}
#endif

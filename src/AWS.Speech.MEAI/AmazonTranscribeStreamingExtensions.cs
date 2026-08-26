// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using AWS.Speech.MEAI;
using Microsoft.Extensions.AI;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Amazon.TranscribeStreaming;

/// <summary>Provides extensions for working with <see cref="IAmazonTranscribeStreaming"/> instances.</summary>
public static class AmazonTranscribeStreamingExtensions
{
    /// <summary>Gets an <see cref="ISpeechToTextClient"/> for the specified <see cref="IAmazonTranscribeStreaming"/> instance.</summary>
    /// <param name="client">The Amazon Transcribe streaming client to represent as an <see cref="ISpeechToTextClient"/>.</param>
    /// <param name="defaultLanguage">The default transcription language. Defaults to <c>en-US</c>.</param>
    /// <param name="defaultSampleRateHertz">The default PCM sample rate in hertz. Defaults to 16000.</param>
    /// <returns>An <see cref="ISpeechToTextClient"/> that transcribes audio with Amazon Transcribe.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Experimental("MEAI001")]
    public static ISpeechToTextClient AsISpeechToTextClient(
        this IAmazonTranscribeStreaming client,
        string? defaultLanguage = "en-US",
        int defaultSampleRateHertz = 16000) =>
        client is not null
            ? new TranscribeSpeechToTextClient(client, defaultLanguage, defaultSampleRateHertz)
            : throw new ArgumentNullException(nameof(client));
}
#endif

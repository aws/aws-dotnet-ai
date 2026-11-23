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
    /// <param name="options">
    /// The default synthesis settings for the client. When <see langword="null"/>, the defaults on
    /// <see cref="PollyTextToSpeechClientOptions"/> are used.
    /// </param>
    /// <returns>An <see cref="ITextToSpeechClient"/> that synthesizes audio with Amazon Polly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    [Experimental("MEAI001")]
    public static ITextToSpeechClient AsITextToSpeechClient(
        this IAmazonPolly client,
        PollyTextToSpeechClientOptions? options = null) =>
        client is not null
            ? new PollyTextToSpeechClient(client, options)
            : throw new ArgumentNullException(nameof(client));
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;

namespace AWS.Speech.MEAI;

/// <summary>Selects which backend drives a <see cref="VoiceAgent"/>.</summary>
[Experimental("MEAI001")]
public enum VoiceAgentBackend
{
    /// <summary>The STT + <c>IChatClient</c> + TTS composition (default).</summary>
    Pipeline,

    /// <summary>Amazon Bedrock Nova Sonic single-model speech-to-speech (wired in a later phase).</summary>
    NovaSonic,
}
#endif

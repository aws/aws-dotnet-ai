// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;

namespace AWS.Speech.MEAI;

/// <summary>Discriminates the payloads a <see cref="VoiceAgent"/> can emit on one ordered stream.</summary>
[Experimental("MEAI001")]
public enum VoiceAgentUpdateKind
{
    /// <summary>A partial speech-to-text transcript, revised as more audio arrives.</summary>
    UserTranscriptPartial,

    /// <summary>A stable speech-to-text transcript that closes a user utterance.</summary>
    UserTranscriptFinal,

    /// <summary>The assistant has begun responding to a user turn.</summary>
    TurnStarted,

    /// <summary>A streamed delta of assistant reply text.</summary>
    AssistantText,

    /// <summary>Synthesized audio for a completed clause of the assistant reply.</summary>
    AssistantAudio,

    /// <summary>The assistant finished the current turn.</summary>
    TurnComplete,

    /// <summary>The in-flight assistant response was cancelled (barge-in in a later phase).</summary>
    Cancelled,
}
#endif

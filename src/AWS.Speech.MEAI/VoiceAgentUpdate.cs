// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Diagnostics.CodeAnalysis;

namespace AWS.Speech.MEAI;

/// <summary>A single item on a <see cref="VoiceAgent"/>'s ordered output stream.</summary>
/// <remarks>
/// One update carries either transcript text (for <see cref="VoiceAgentUpdateKind.UserTranscriptPartial"/>
/// and <see cref="VoiceAgentUpdateKind.UserTranscriptFinal"/>), a delta of assistant reply text (for
/// <see cref="VoiceAgentUpdateKind.AssistantText"/>), or a chunk of synthesized audio (for
/// <see cref="VoiceAgentUpdateKind.AssistantAudio"/>). Turn-lifecycle kinds carry no payload.
/// Audio is 16-bit signed little-endian mono PCM at <c>OutputSampleRateHertz</c>.
/// </remarks>
[Experimental("MEAI001")]
public readonly record struct VoiceAgentUpdate
{
    /// <summary>The kind of update this instance carries.</summary>
    public VoiceAgentUpdateKind Kind { get; init; }

    /// <summary>Transcript text or an assistant reply text delta, or <see langword="null"/>.</summary>
    public string? Text { get; init; }

    /// <summary>Synthesized audio bytes for a completed clause, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Audio { get; init; }

    /// <summary><see langword="true"/> when a transcript update is a stable final; ignored for other kinds.</summary>
    public bool IsFinal { get; init; }

    /// <summary>Token accounting for the current turn, when available on turn completion.</summary>
    public UsageDetails? Usage { get; init; }

    /// <summary>The chat response ID for the current turn, when the underlying <c>IChatClient</c> reports one.</summary>
    public string? ResponseId { get; init; }
}
#endif

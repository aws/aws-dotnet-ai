// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;

namespace AWS.Speech.MEAI;

/// <summary>
/// Translates between the <see cref="VoiceAgentUpdate"/> stream and MEAI's realtime message vocabulary,
/// so the pipeline can be exposed as an <see cref="IRealtimeClient"/> and a Nova Sonic session can be
/// consumed as a <see cref="VoiceAgent"/>. The message types line up; timing and cadence do not.
/// </summary>
internal static class RealtimeMessageMapper
{
    /// <summary>Maps a realtime server message to a <see cref="VoiceAgentUpdate"/>, or <see langword="null"/> to skip it.</summary>
    public static VoiceAgentUpdate? ToVoiceAgentUpdate(RealtimeServerMessage message)
    {
        var type = message.Type.Value;

        if (type == RealtimeServerMessageType.InputAudioTranscriptionDelta.Value &&
            message is InputAudioTranscriptionRealtimeServerMessage partial)
        {
            return new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptPartial, Text = partial.Transcription };
        }

        if (type == RealtimeServerMessageType.InputAudioTranscriptionCompleted.Value &&
            message is InputAudioTranscriptionRealtimeServerMessage final)
        {
            return new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptFinal, Text = final.Transcription, IsFinal = true };
        }

        if (type == RealtimeServerMessageType.ResponseCreated.Value && message is ResponseCreatedRealtimeServerMessage created)
        {
            return new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnStarted, ResponseId = created.ResponseId };
        }

        if (type == RealtimeServerMessageType.OutputTextDelta.Value && message is OutputTextAudioRealtimeServerMessage text)
        {
            return new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantText, Text = text.Text, ResponseId = text.ResponseId };
        }

        if (type == RealtimeServerMessageType.OutputAudioDelta.Value && message is OutputTextAudioRealtimeServerMessage audio)
        {
            var bytes = string.IsNullOrEmpty(audio.Audio) ? Array.Empty<byte>() : Convert.FromBase64String(audio.Audio);
            return new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantAudio, Audio = bytes, ResponseId = audio.ResponseId };
        }

        if (type == RealtimeServerMessageType.ResponseDone.Value && message is ResponseCreatedRealtimeServerMessage done)
        {
            var cancelled = string.Equals(done.Status, RealtimeResponseStatus.Cancelled, StringComparison.Ordinal);
            return new VoiceAgentUpdate
            {
                Kind = cancelled ? VoiceAgentUpdateKind.Cancelled : VoiceAgentUpdateKind.TurnComplete,
                Usage = done.Usage,
                ResponseId = done.ResponseId,
            };
        }

        return null;
    }

    /// <summary>Maps a <see cref="VoiceAgentUpdate"/> to a realtime server message, or <see langword="null"/> to skip it.</summary>
    public static RealtimeServerMessage? ToServerMessage(VoiceAgentUpdate update)
    {
        switch (update.Kind)
        {
            case VoiceAgentUpdateKind.UserTranscriptPartial:
                return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionDelta)
                {
                    Transcription = update.Text,
                };

            case VoiceAgentUpdateKind.UserTranscriptFinal:
                return new InputAudioTranscriptionRealtimeServerMessage(RealtimeServerMessageType.InputAudioTranscriptionCompleted)
                {
                    Transcription = update.Text,
                };

            case VoiceAgentUpdateKind.TurnStarted:
                return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated)
                {
                    ResponseId = update.ResponseId,
                };

            case VoiceAgentUpdateKind.AssistantText:
                return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputTextDelta)
                {
                    Text = update.Text,
                    ResponseId = update.ResponseId,
                };

            case VoiceAgentUpdateKind.AssistantAudio:
                return new OutputTextAudioRealtimeServerMessage(RealtimeServerMessageType.OutputAudioDelta)
                {
                    Audio = update.Audio is { } pcm ? Convert.ToBase64String(pcm.ToArray()) : string.Empty,
                    ResponseId = update.ResponseId,
                };

            case VoiceAgentUpdateKind.TurnComplete:
                return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
                {
                    Status = RealtimeResponseStatus.Completed,
                    Usage = update.Usage,
                    ResponseId = update.ResponseId,
                };

            case VoiceAgentUpdateKind.Cancelled:
                return new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
                {
                    Status = RealtimeResponseStatus.Cancelled,
                    ResponseId = update.ResponseId,
                };

            default:
                return null;
        }
    }
}
#endif

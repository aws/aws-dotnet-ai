// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using Xunit;

namespace AWS.Speech.MEAI;

public class RealtimeMessageMapperTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public void RoundTrip_TranscriptsTextAndLifecycle()
    {
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptPartial, Text = "hel" });
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.UserTranscriptFinal, Text = "hello", IsFinal = true });
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnStarted, ResponseId = "r1" });
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantText, Text = "Hi.", ResponseId = "r1" });
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.TurnComplete, ResponseId = "r1" });
        AssertRoundTrips(new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.Cancelled, ResponseId = "r1" });
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void RoundTrip_AssistantAudio_PreservesBytesViaBase64()
    {
        var bytes = new byte[] { 1, 2, 3, 250, 128, 0 };
        var message = RealtimeMessageMapper.ToServerMessage(
            new VoiceAgentUpdate { Kind = VoiceAgentUpdateKind.AssistantAudio, Audio = bytes, ResponseId = "r1" });

        var audioMessage = Assert.IsType<OutputTextAudioRealtimeServerMessage>(message);
        Assert.Equal(RealtimeServerMessageType.OutputAudioDelta.Value, audioMessage.Type.Value);
        Assert.Equal(Convert.ToBase64String(bytes), audioMessage.Audio);

        var back = RealtimeMessageMapper.ToVoiceAgentUpdate(audioMessage);
        Assert.NotNull(back);
        Assert.Equal(VoiceAgentUpdateKind.AssistantAudio, back!.Value.Kind);
        Assert.Equal(bytes, back.Value.Audio!.Value.ToArray());
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void ToVoiceAgentUpdate_ResponseDoneCancelled_MapsToCancelled()
    {
        var message = new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
        {
            Status = RealtimeResponseStatus.Cancelled,
            ResponseId = "r1",
        };

        var update = RealtimeMessageMapper.ToVoiceAgentUpdate(message);
        Assert.NotNull(update);
        Assert.Equal(VoiceAgentUpdateKind.Cancelled, update!.Value.Kind);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void ToVoiceAgentUpdate_UnknownType_ReturnsNull()
    {
        var message = new ErrorRealtimeServerMessage { Type = RealtimeServerMessageType.Error };
        Assert.Null(RealtimeMessageMapper.ToVoiceAgentUpdate(message));
    }

    private static void AssertRoundTrips(VoiceAgentUpdate original)
    {
        var message = RealtimeMessageMapper.ToServerMessage(original);
        Assert.NotNull(message);

        var back = RealtimeMessageMapper.ToVoiceAgentUpdate(message!);
        Assert.NotNull(back);
        Assert.Equal(original.Kind, back!.Value.Kind);
        Assert.Equal(original.Text, back.Value.Text);
        Assert.Equal(original.ResponseId, back.Value.ResponseId);
    }
}
#endif

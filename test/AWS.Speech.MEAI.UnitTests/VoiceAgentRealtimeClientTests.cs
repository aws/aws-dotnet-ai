// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI;

public class VoiceAgentRealtimeClientTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task AsIRealtimeClient_DrivesPipelineAndEmitsServerMessages()
    {
        var stt = new ScriptedSpeechToText(
            new SpeechToTextResponseUpdate("hello there") { Kind = SpeechToTextResponseUpdateKind.TextUpdated });
        var chat = new ScriptedChat("Hi.");
        var tts = new EchoTextToSpeech();

        await using var agent = new VoiceAgent(stt, chat, tts);
        using var realtime = agent.AsIRealtimeClient("amazon.nova-sonic-v1:0");

        var session = await realtime.CreateSessionAsync(
            new RealtimeSessionOptions { Model = "amazon.nova-sonic-v1:0" }, CancellationToken.None);

        // Push a little audio (the scripted STT ignores it, but this exercises the input path).
        await session.SendAsync(
            new InputAudioBufferAppendRealtimeClientMessage(new DataContent(new byte[] { 1, 2, 3 }, "audio/lpcm")),
            CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var messages = new List<RealtimeServerMessage>();
        await foreach (var message in session.GetStreamingResponseAsync(timeout.Token))
        {
            messages.Add(message);
        }

        await session.DisposeAsync();

        // The user final, turn start, assistant text, assistant audio, and turn done all surface as
        // realtime server messages.
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.InputAudioTranscriptionCompleted.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.ResponseCreated.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.OutputTextDelta.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.OutputAudioDelta.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.ResponseDone.Value);

        Assert.Equal("amazon.nova-sonic-v1:0", session.Options.Model);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task NovaSonicBackend_ViaProviderNeutralConstructor_Throws()
    {
        // The provider-neutral constructor is pipeline-only; NovaSonic requires VoiceAgent.Create.
        await using var agent = new VoiceAgent(
            new ScriptedSpeechToText(), new ScriptedChat("x"), new EchoTextToSpeech(),
            new VoiceAgentOptions { Backend = VoiceAgentBackend.NovaSonic });

        using var mic = new MemoryStream(new byte[] { 0 });
        Assert.Throws<NotSupportedException>(() => agent.RunAsync(mic));
    }
}
#endif

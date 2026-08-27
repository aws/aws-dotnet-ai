// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon.Polly;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI.IntegrationTests;

/// <summary>
/// Live integration test for the <see cref="IRealtimeClient"/> adapter over the pipeline
/// (<c>VoiceAgent.AsIRealtimeClient</c>). Pushes audio through the realtime session's
/// <c>SendAsync</c> path and reads back mapped realtime server messages, exercising Amazon Transcribe,
/// Amazon Bedrock, and Amazon Polly through the realtime contract. Always runs against live AWS.
/// </summary>
[Trait("Category", "Integration")]
public class RealtimeAdapterIntegrationTests
{
    private static string ModelId =>
        Environment.GetEnvironmentVariable("SPEECH_MEAI_TEST_MODEL_ID")
        ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    [Fact]
    [Trait("IntegrationTest", "Speech")]
    public async Task AsIRealtimeClient_RunsPipelineThroughRealtimeContract()
    {
        using var polly = new AmazonPollyClient();
        var tts = polly.AsITextToSpeechClient(defaultSampleRateHertz: 16000);
        var question = await tts.GetAudioAsync(
            "In one word, what color is a clear daytime sky?", cancellationToken: TestContext.Current.CancellationToken);
        var speech = question.Contents.OfType<DataContent>().Single().Data.ToArray();

        var pcm = new byte[speech.Length + 32000]; // ~1s trailing silence so Transcribe endpoints
        Array.Copy(speech, pcm, speech.Length);

        await using var agent = VoiceAgent.Create(options =>
        {
            options.ModelId = ModelId;
            options.Instructions = "You are concise. Answer in one short sentence.";
        });

        using var realtime = agent.AsIRealtimeClient(ModelId);
        var session = await realtime.CreateSessionAsync(
            new RealtimeSessionOptions { Model = ModelId }, TestContext.Current.CancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));

        var messages = new List<RealtimeServerMessage>();
        var reader = Task.Run(async () =>
        {
            await foreach (var message in session.GetStreamingResponseAsync(timeout.Token))
            {
                messages.Add(message);
            }
        });

        await session.SendAsync(
            new InputAudioBufferAppendRealtimeClientMessage(new DataContent(pcm, "audio/lpcm")), timeout.Token);
        await session.DisposeAsync();   // end of audio -> pipeline drains and the enumeration completes
        await reader;

        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.InputAudioTranscriptionCompleted.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.OutputTextDelta.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.OutputAudioDelta.Value);
        Assert.Contains(messages, m => m.Type.Value == RealtimeServerMessageType.ResponseDone.Value);
    }
}
#endif

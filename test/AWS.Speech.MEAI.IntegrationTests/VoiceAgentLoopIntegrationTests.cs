// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon.Polly;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI.IntegrationTests;

/// <summary>
/// Live end-to-end integration test for the <see cref="VoiceAgent"/> pipeline: Amazon Transcribe for
/// speech-to-text, Amazon Bedrock for reasoning, and Amazon Polly for text-to-speech. Always runs
/// against live AWS with the ambient credential and region chain.
/// </summary>
[Trait("Category", "Integration")]
public class VoiceAgentLoopIntegrationTests
{
    // Overridable so the test tracks a current, invokable model as Bedrock's catalog changes.
    private static string ModelId =>
        Environment.GetEnvironmentVariable("SPEECH_MEAI_TEST_MODEL_ID")
        ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0";

    [Fact]
    [Trait("IntegrationTest", "Speech")]
    public async Task RunAsync_TranscribesReasonsAndSpeaks()
    {
        // Speak a question with Amazon Polly, then feed it through the full loop. A second of trailing
        // silence lets Transcribe endpoint the utterance so the agent takes its turn.
        using var polly = new AmazonPollyClient();
        var tts = polly.AsITextToSpeechClient(defaultSampleRateHertz: 16000);
        var question = await tts.GetAudioAsync(
            "In one word, what color is a clear daytime sky?", cancellationToken: TestContext.Current.CancellationToken);
        var speech = question.Contents.OfType<DataContent>().Single().Data.ToArray();

        var pcm = new byte[speech.Length + 32000]; // ~1s trailing silence at 16 kHz mono 16-bit
        Array.Copy(speech, pcm, speech.Length);

        await using var agent = VoiceAgent.Create(options =>
        {
            options.ModelId = ModelId;
            options.Instructions = "You are concise. Answer in one short sentence.";
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));

        var kinds = new HashSet<VoiceAgentUpdateKind>();
        var assistantText = new System.Text.StringBuilder();
        long assistantAudioBytes = 0;
        string? userFinal = null;

        using var microphone = new MemoryStream(pcm);
        await foreach (var update in agent.RunAsync(microphone, timeout.Token))
        {
            kinds.Add(update.Kind);
            switch (update.Kind)
            {
                case VoiceAgentUpdateKind.UserTranscriptFinal:
                    userFinal = update.Text;
                    break;
                case VoiceAgentUpdateKind.AssistantText when update.Text is { Length: > 0 }:
                    assistantText.Append(update.Text);
                    break;
                case VoiceAgentUpdateKind.AssistantAudio when update.Audio is { } audio:
                    assistantAudioBytes += audio.Length;
                    break;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(userFinal), "The caller's utterance was not transcribed.");
        Assert.Contains(VoiceAgentUpdateKind.TurnStarted, kinds);
        Assert.Contains(VoiceAgentUpdateKind.TurnComplete, kinds);
        Assert.False(string.IsNullOrWhiteSpace(assistantText.ToString()), "The agent produced no reply text.");
        Assert.True(assistantAudioBytes > 0, "The agent produced no spoken audio.");
    }
}
#endif

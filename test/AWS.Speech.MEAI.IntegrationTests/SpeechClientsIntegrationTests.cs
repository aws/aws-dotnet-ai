// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Polly;
using Amazon.TranscribeStreaming;
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
/// Live integration tests for the standalone speech clients. These call Amazon Polly and Amazon
/// Transcribe with the ambient AWS credential and region chain and always run (they are not gated on
/// credentials): a missing or unauthorized environment fails the test rather than skipping it.
/// </summary>
[Trait("Category", "Integration")]
public class SpeechClientsIntegrationTests
{
    private const string SpokenPhrase = "the quick brown fox jumps over the lazy dog";
    private static readonly string[] DistinctiveWords = { "quick", "brown", "fox", "lazy", "dog" };

    [Fact]
    [Trait("IntegrationTest", "Speech")]
    public async Task Polly_GetAudioAsync_ReturnsPcmAudio()
    {
        using var polly = new AmazonPollyClient();
        var tts = polly.AsITextToSpeechClient();

        var response = await tts.GetAudioAsync("Hello from Amazon Polly.", cancellationToken: TestContext.Current.CancellationToken);

        var audio = Assert.IsType<DataContent>(Assert.Single(response.Contents));
        Assert.Equal("audio/lpcm", audio.MediaType);
        Assert.False(audio.Data.IsEmpty, "Amazon Polly returned no audio.");
    }

    [Fact]
    [Trait("IntegrationTest", "Speech")]
    public async Task Polly_GetStreamingAudioAsync_EmitsSessionFramedChunks()
    {
        using var polly = new AmazonPollyClient();
        var tts = polly.AsITextToSpeechClient();

        var kinds = new List<TextToSpeechResponseUpdateKind>();
        long audioBytes = 0;
        await foreach (var update in tts.GetStreamingAudioAsync("Streaming from Amazon Polly.")
                           .WithCancellation(TestContext.Current.CancellationToken))
        {
            kinds.Add(update.Kind);
            foreach (var content in update.Contents.OfType<DataContent>())
            {
                audioBytes += content.Data.Length;
            }
        }

        Assert.Equal(TextToSpeechResponseUpdateKind.SessionOpen, kinds.First());
        Assert.Equal(TextToSpeechResponseUpdateKind.SessionClose, kinds.Last());
        Assert.Contains(TextToSpeechResponseUpdateKind.AudioUpdating, kinds);
        Assert.True(audioBytes > 0, "Amazon Polly streamed no audio.");
    }

    [Fact]
    [Trait("IntegrationTest", "Speech")]
    public async Task Transcribe_TranscribesPollyGeneratedSpeech()
    {
        // Synthesize the phrase with Amazon Polly (16 kHz mono PCM), then transcribe it with Amazon
        // Transcribe. This exercises both clients end to end without a checked-in audio fixture.
        using var polly = new AmazonPollyClient();
        var tts = polly.AsITextToSpeechClient(defaultSampleRateHertz: 16000);
        var synthesized = await tts.GetAudioAsync(SpokenPhrase, cancellationToken: TestContext.Current.CancellationToken);
        var speech = synthesized.Contents.OfType<DataContent>().Single().Data.ToArray();
        Assert.True(speech.Length > 0, "Amazon Polly returned no audio to transcribe.");

        // Append ~1s of trailing silence (16 kHz, 16-bit mono => 32000 bytes) so Amazon Transcribe
        // detects end-of-utterance and stabilizes a final result rather than only partials.
        var pcm = new byte[speech.Length + 32000];
        Array.Copy(speech, pcm, speech.Length);

        using var transcribe = new AmazonTranscribeStreamingClient();
        var stt = transcribe.AsISpeechToTextClient("en-US", 16000);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));

        string? latestRecognized = null;
        bool sawFinal = false;
        using var audioStream = new MemoryStream(pcm);
        await foreach (var update in stt.GetStreamingTextAsync(audioStream, cancellationToken: timeout.Token))
        {
            if ((update.Kind == SpeechToTextResponseUpdateKind.TextUpdating ||
                 update.Kind == SpeechToTextResponseUpdateKind.TextUpdated) && !string.IsNullOrEmpty(update.Text))
            {
                latestRecognized = update.Text;   // successive results refine the same utterance
                sawFinal |= update.Kind == SpeechToTextResponseUpdateKind.TextUpdated;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(latestRecognized), "Amazon Transcribe recognized no speech.");
        Assert.True(sawFinal, "Amazon Transcribe produced no final (TextUpdated) result.");
        var transcript = latestRecognized!.ToLowerInvariant();
        Assert.Contains(DistinctiveWords, word => transcript.Contains(word, StringComparison.Ordinal));
    }
}

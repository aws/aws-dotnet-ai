// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Polly;
using Amazon.Polly.Model;
using Microsoft.Extensions.AI;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI;

public class PollyTextToSpeechClientTests
{
    private static readonly byte[] SampleAudio = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

    private static (Mock<IAmazonPolly> Mock, List<SynthesizeSpeechRequest> Captured) CreateMock()
    {
        var captured = new List<SynthesizeSpeechRequest>();
        var mock = new Mock<IAmazonPolly>(MockBehavior.Strict);
        mock.Setup(p => p.SynthesizeSpeechAsync(It.IsAny<SynthesizeSpeechRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SynthesizeSpeechRequest, CancellationToken>((r, _) => captured.Add(r))
            .ReturnsAsync(() => new SynthesizeSpeechResponse { AudioStream = new MemoryStream(SampleAudio) });
        return (mock, captured);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void AsITextToSpeechClient_NullClient_Throws()
    {
        IAmazonPolly client = null!;
        Assert.Throws<ArgumentNullException>(() => client.AsITextToSpeechClient());
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetAudioAsync_ReturnsLpcmDataContent()
    {
        var (mock, captured) = CreateMock();
        ITextToSpeechClient tts = mock.Object.AsITextToSpeechClient();

        var response = await tts.GetAudioAsync("Your table is ready.");

        var data = Assert.IsType<DataContent>(Assert.Single(response.Contents));
        Assert.Equal("audio/lpcm", data.MediaType);
        Assert.Equal(SampleAudio, data.Data.ToArray());

        var request = Assert.Single(captured);
        Assert.Equal("Your table is ready.", request.Text);
        Assert.Equal(OutputFormat.Pcm.Value, request.OutputFormat.Value);
        Assert.Equal(VoiceId.Matthew.Value, request.VoiceId.Value);
        Assert.Equal(Engine.Neural.Value, request.Engine.Value);
        Assert.Equal("16000", request.SampleRate);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetStreamingAudioAsync_EmitsSessionOpenChunksAndClose()
    {
        var (mock, _) = CreateMock();
        ITextToSpeechClient tts = mock.Object.AsITextToSpeechClient();

        var kinds = new List<TextToSpeechResponseUpdateKind>();
        using var audio = new MemoryStream();
        await foreach (var update in tts.GetStreamingAudioAsync("hello"))
        {
            kinds.Add(update.Kind);
            foreach (var content in update.Contents.OfType<DataContent>())
            {
                var bytes = content.Data.ToArray();
                audio.Write(bytes, 0, bytes.Length);
            }
        }

        Assert.Equal(TextToSpeechResponseUpdateKind.SessionOpen, kinds.First());
        Assert.Equal(TextToSpeechResponseUpdateKind.SessionClose, kinds.Last());
        Assert.Contains(TextToSpeechResponseUpdateKind.AudioUpdating, kinds);
        Assert.Equal(SampleAudio, audio.ToArray());
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetAudioAsync_UnsupportedPcmSampleRate_Throws()
    {
        var (mock, _) = CreateMock();
        ITextToSpeechClient tts = mock.Object.AsITextToSpeechClient(defaultSampleRateHertz: 24000);

        await Assert.ThrowsAsync<ArgumentException>(() => tts.GetAudioAsync("hi"));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetAudioAsync_OptionsVoiceId_OverridesDefault()
    {
        var (mock, captured) = CreateMock();
        ITextToSpeechClient tts = mock.Object.AsITextToSpeechClient();

        await tts.GetAudioAsync("hi", new TextToSpeechOptions { VoiceId = "Joanna" });

        Assert.Equal("Joanna", Assert.Single(captured).VoiceId.Value);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void GetService_ReturnsUnderlyingPollyClient()
    {
        var (mock, _) = CreateMock();
        ITextToSpeechClient tts = mock.Object.AsITextToSpeechClient();

        Assert.Same(mock.Object, tts.GetService(typeof(IAmazonPolly)));
        Assert.Same(tts, tts.GetService(typeof(ITextToSpeechClient)));
        Assert.Null(tts.GetService(typeof(string)));
    }
}

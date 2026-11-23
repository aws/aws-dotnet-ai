// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon.TranscribeStreaming;
using Amazon.TranscribeStreaming.Model;
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

public class TranscribeSpeechToTextClientTests
{
    private static readonly byte[] SampleAudio = { 10, 20, 30, 40, 50 };

    private static (Mock<IAmazonTranscribeStreaming> Mock, List<StartStreamTranscriptionRequest> Captured) CreateMock()
    {
        var captured = new List<StartStreamTranscriptionRequest>();
        var mock = new Mock<IAmazonTranscribeStreaming>(MockBehavior.Strict);
        mock.Setup(c => c.StartStreamTranscriptionAsync(
                It.IsAny<StartStreamTranscriptionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StartStreamTranscriptionRequest, CancellationToken>((r, _) => captured.Add(r))
            .ReturnsAsync(() => new StartStreamTranscriptionResponse());   // TranscriptResultStream is null
        return (mock, captured);
    }

    private static async Task<List<SpeechToTextResponseUpdate>> CollectAsync(
        IAsyncEnumerable<SpeechToTextResponseUpdate> updates)
    {
        var list = new List<SpeechToTextResponseUpdate>();
        await foreach (var update in updates) list.Add(update);
        return list;
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void AsISpeechToTextClient_NullClient_Throws()
    {
        IAmazonTranscribeStreaming client = null!;
        Assert.Throws<ArgumentNullException>(() => client.AsISpeechToTextClient());
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetStreamingTextAsync_BuildsPcmRequestAndBracketsWithSession()
    {
        var (mock, captured) = CreateMock();
        ISpeechToTextClient stt = mock.Object.AsISpeechToTextClient();

        using var input = new MemoryStream(SampleAudio);
        var updates = await CollectAsync(stt.GetStreamingTextAsync(input));

        Assert.Equal(SpeechToTextResponseUpdateKind.SessionOpen, updates.First().Kind);
        Assert.Equal(SpeechToTextResponseUpdateKind.SessionClose, updates.Last().Kind);

        var request = Assert.Single(captured);
        Assert.Equal("en-US", request.LanguageCode.Value);
        Assert.Equal(MediaEncoding.Pcm.Value, request.MediaEncoding.Value);
        Assert.Equal(16000, request.MediaSampleRateHertz);
        Assert.NotNull(request.AudioStreamPublisher);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task AudioStreamPublisher_YieldsChunkThenNullAtEnd()
    {
        var (mock, captured) = CreateMock();
        ISpeechToTextClient stt = mock.Object.AsISpeechToTextClient();

        using var input = new MemoryStream(SampleAudio);
        await CollectAsync(stt.GetStreamingTextAsync(input));   // null result stream leaves the publisher unused

        var publisher = Assert.Single(captured).AudioStreamPublisher;

        var first = await publisher();
        var audioEvent = Assert.IsType<AudioEvent>(first);
        Assert.Equal(SampleAudio, audioEvent.AudioChunk.ToArray());

        Assert.Null(await publisher());   // stream drained -> end of audio
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task GetStreamingTextAsync_SpeechLanguageOption_OverridesDefault()
    {
        var (mock, captured) = CreateMock();
        ISpeechToTextClient stt = mock.Object.AsISpeechToTextClient();

        using var input = new MemoryStream(SampleAudio);
        await CollectAsync(stt.GetStreamingTextAsync(input, new SpeechToTextOptions { SpeechLanguage = "es-US" }));

        Assert.Equal("es-US", Assert.Single(captured).LanguageCode.Value);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void TranslateTranscript_MapsPartialAndFinalAndSkipsEmpty()
    {
        var transcriptEvent = new TranscriptEvent
        {
            Transcript = new Transcript
            {
                Results = new List<Result>
                {
                    new Result
                    {
                        IsPartial = true,
                        StartTime = 0.5,
                        EndTime = 1.5,
                        Alternatives = new List<Alternative> { new Alternative { Transcript = "hello" } },
                    },
                    new Result
                    {
                        IsPartial = false,
                        Alternatives = new List<Alternative> { new Alternative { Transcript = "hello world" } },
                    },
                    new Result
                    {
                        IsPartial = false,
                        Alternatives = new List<Alternative>(),   // no text -> skipped
                    },
                },
            },
        };

        var updates = TranscribeSpeechToTextClient.TranslateTranscript(transcriptEvent).ToList();

        Assert.Equal(2, updates.Count);
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdating, updates[0].Kind);
        Assert.Equal("hello", updates[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), updates[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(1.5), updates[0].EndTime);
        Assert.Equal(SpeechToTextResponseUpdateKind.TextUpdated, updates[1].Kind);
        Assert.Equal("hello world", updates[1].Text);
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void GetService_ReturnsUnderlyingTranscribeClient()
    {
        var (mock, _) = CreateMock();
        ISpeechToTextClient stt = mock.Object.AsISpeechToTextClient();

        Assert.Same(mock.Object, stt.GetService(typeof(IAmazonTranscribeStreaming)));
        Assert.Same(stt, stt.GetService(typeof(ISpeechToTextClient)));
    }
}
#endif

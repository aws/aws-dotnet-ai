// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AWS.Speech.MEAI;

public class VoiceAgentServiceCollectionExtensionsTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public void AddVoiceAgent_NullServices_Throws()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() => services.AddVoiceAgent());
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task AddVoiceAgent_WithPrebuiltClients_ResolvesVoiceAgentComposingThem()
    {
        var stt = new NoopStt();
        var chat = new NoopChat();
        var tts = new NoopTts();

        var services = new ServiceCollection();
        services.AddVoiceAgent(o =>
        {
            o.SpeechToTextClient = stt;
            o.ChatClient = chat;
            o.TextToSpeechClient = tts;
        });

        await using var provider = services.BuildServiceProvider();
        var agent = provider.GetRequiredService<VoiceAgent>();

        Assert.Same(stt, agent.GetService(typeof(ISpeechToTextClient)));
        Assert.Same(chat, agent.GetService(typeof(IChatClient)));
        Assert.Same(tts, agent.GetService(typeof(ITextToSpeechClient)));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public async Task AddVoiceAgent_PreRegisteredChatClient_Wins()
    {
        var myChat = new NoopChat();

        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(myChat);   // caller registered their own reasoning model
        services.AddVoiceAgent(o =>
        {
            o.SpeechToTextClient = new NoopStt();
            o.TextToSpeechClient = new NoopTts();
            // No ChatClient override and no ModelId: the pre-registered IChatClient must win via TryAdd.
        });

        await using var provider = services.BuildServiceProvider();
        Assert.Same(myChat, provider.GetRequiredService<IChatClient>());
        Assert.Same(myChat, provider.GetRequiredService<VoiceAgent>().GetService(typeof(IChatClient)));
    }

    // ---- minimal no-op MEAI clients ----

    private sealed class NoopStt : ISpeechToTextClient
    {
        public Task<SpeechToTextResponse> GetTextAsync(Stream a, SpeechToTextOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

#pragma warning disable CS1998
        public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream a, SpeechToTextOptions? o, [EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
#pragma warning restore CS1998

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class NoopChat : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

#pragma warning disable CS1998
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o, [EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
#pragma warning restore CS1998

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class NoopTts : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(string t, TextToSpeechOptions? o, CancellationToken ct) =>
            throw new NotImplementedException();

#pragma warning disable CS1998
        public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string t, TextToSpeechOptions? o, [EnumeratorCancellation] CancellationToken ct)
        {
            yield break;
        }
#pragma warning restore CS1998

        public object? GetService(System.Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
#endif

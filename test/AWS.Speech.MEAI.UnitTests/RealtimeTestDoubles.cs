// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AWS.Speech.MEAI;

// Shared MEAI client stubs for the realtime-adapter tests. These ignore the caller's audio stream and
// simply replay a scripted transcript / reply / echoed audio, which is enough to exercise the pipeline.
internal sealed class ScriptedSpeechToText : ISpeechToTextClient
{
    private readonly SpeechToTextResponseUpdate[] _updates;
    public ScriptedSpeechToText(params SpeechToTextResponseUpdate[] updates) => _updates = updates;

    public Task<SpeechToTextResponse> GetTextAsync(Stream a, SpeechToTextOptions? o, CancellationToken ct) =>
        throw new NotImplementedException();

    public async IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream, SpeechToTextOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var u in _updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return u;
            await Task.Yield();
        }
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}

internal sealed class ScriptedChat : IChatClient
{
    private readonly string _reply;
    public ScriptedChat(string reply) => _reply = reply;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o, CancellationToken ct) =>
        throw new NotImplementedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, _reply) { ResponseId = "resp-1" };
        await Task.Yield();
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}

internal sealed class EchoTextToSpeech : ITextToSpeechClient
{
    public Task<TextToSpeechResponse> GetAudioAsync(string t, TextToSpeechOptions? o, CancellationToken ct) =>
        throw new NotImplementedException();

    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text, TextToSpeechOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        yield return new TextToSpeechResponseUpdate(new List<AIContent> { new DataContent(bytes, "audio/lpcm") })
        {
            Kind = TextToSpeechResponseUpdateKind.AudioUpdating,
        };
        await Task.Yield();
    }

    public object? GetService(System.Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
#endif

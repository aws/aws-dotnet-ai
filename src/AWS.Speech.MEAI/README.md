# AWS.Speech.MEAI

Microsoft.Extensions.AI (MEAI) speech clients over Amazon Transcribe and Amazon Polly, plus a `VoiceAgent` that composes speech-to-text, an `IChatClient`, and text-to-speech into a full-duplex voice loop.

The package provides:

- **`TranscribeSpeechToTextClient`** — an `ISpeechToTextClient` over Amazon Transcribe streaming.
- **`PollyTextToSpeechClient`** — an `ITextToSpeechClient` over Amazon Polly.
- **`VoiceAgent`** — a facade that runs the "listen, reason, speak" loop and emits one ordered stream of `VoiceAgentUpdate`s, with Transcribe-driven turn detection and barge-in.

The speech types are evaluation-stage MEAI APIs, so they carry `[Experimental("MEAI001")]`. Suppress `MEAI001` where you use them.

## What this package provides per target framework

The surface depends on the target framework:

| Target framework | What this package provides |
| --- | --- |
| **net8.0** | Everything: Polly TTS, Transcribe STT, the full `VoiceAgent` loop, barge-in, the Nova Sonic backend, and the `AsIRealtimeClient()` adapter. |
| **net472, netstandard2.0** | The standalone Polly `ITextToSpeechClient` (`AsITextToSpeechClient()`) only. Amazon Transcribe's real-time API requires HTTP/2, which .NET Framework 4.7.2 lacks, so STT and the loop are net8.0-only. |

## The five-line voice agent (net8.0)

```csharp
using AWS.Speech.MEAI;

// Default AWS credential + region chain. Defaults: Transcribe en-US 16 kHz PCM in,
// Polly "Matthew" Neural 16 kHz PCM out, barge-in on. Just pick a Bedrock model to reason with.
await using var agent = VoiceAgent.Create(o => o.ModelId = "anthropic.claude-3-5-haiku-20241022-v1:0");

await foreach (var update in agent.RunAsync(microphonePcmStream))   // 16 kHz mono 16-bit PCM in
    if (update.Audio is { } pcm) speaker.Write(pcm.Span);          // spoken reply, 16 kHz mono PCM
```

Each item on the stream is a `VoiceAgentUpdate` discriminated by `Kind`: `UserTranscriptPartial`, `UserTranscriptFinal`, `TurnStarted`, `AssistantText`, `AssistantAudio`, `TurnComplete`, and `Cancelled` (barge-in).

## Speech clients on their own

Each MEAI client is useful without the loop. Polly TTS runs on every target framework:

```csharp
using Amazon.Polly;

ITextToSpeechClient tts = new AmazonPollyClient().AsITextToSpeechClient();
await foreach (var update in tts.GetStreamingAudioAsync("Your table is ready."))
    if (update.Contents.OfType<DataContent>().FirstOrDefault() is { } pcm)
        speaker.Write(pcm.Data.Span);   // 16 kHz mono 16-bit PCM
```

Transcribe STT (net8.0) mirrors the idiom with `new AmazonTranscribeStreamingClient().AsISpeechToTextClient()`.

## Bring your own IChatClient

The loop composes any MEAI `IChatClient`, so the reasoning step is not tied to Bedrock:

```csharp
using Amazon.Polly;
using Amazon.TranscribeStreaming;

var stt  = new AmazonTranscribeStreamingClient().AsISpeechToTextClient();
var tts  = new AmazonPollyClient().AsITextToSpeechClient();
var chat = new SomeOtherChatClient(...);   // any IChatClient

await using var agent = new VoiceAgent(stt, chat, tts,
    new VoiceAgentOptions { Instructions = "You are a clinic intake assistant." });

await foreach (var update in agent.RunAsync(microphonePcmStream))
{
    if (update.Kind == VoiceAgentUpdateKind.UserTranscriptFinal) Console.WriteLine($"caller: {update.Text}");
    if (update.Audio is { } pcm) speaker.Write(pcm.Span);
}
```

## Dependency injection

`AddVoiceAgent` registers the agent and the MEAI clients it composes. Every registration uses `TryAdd`, so your own registration of any leg wins:

```csharp
builder.Services.AddVoiceAgent(o =>
{
    o.ModelId       = "anthropic.claude-3-5-haiku-20241022-v1:0";
    o.Instructions  = "You are a clinic intake assistant.";
    o.EnableBargeIn = true;
});
// Resolve VoiceAgent from the container wherever you need it.
```

## Swap to native Nova Sonic

Same call sites; one option change moves reasoning-plus-speech onto the single-model backend:

```csharp
await using var agent = VoiceAgent.Create(o =>
{
    o.Backend = VoiceAgentBackend.NovaSonic;   // was Pipeline
    o.ModelId = "amazon.nova-sonic-v1:0";
});
// RunAsync and the VoiceAgentUpdate stream are unchanged.
```

`agent.AsIRealtimeClient()` exposes either backend through MEAI's `IRealtimeClient` contract. The message types match Amazon Bedrock Nova Sonic's realtime surface; timing and cadence differ.

## Audio format

The whole path standardizes on **16 kHz, 16-bit signed little-endian, mono PCM** (`audio/lpcm`), the one sample rate common to Transcribe's recommended input and Polly's PCM output, so there is no resampling between microphone, model, and speaker.

## Sample

See [`sampleapps/VoiceAgentWavSample`](https://github.com/aws/aws-dotnet-ai/tree/main/sampleapps/VoiceAgentWavSample) for a runnable WAV-in, WAV-out console app.

## License

Apache-2.0. See `LICENSE`.

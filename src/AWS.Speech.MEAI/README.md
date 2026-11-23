# AWS.Speech.MEAI

Microsoft.Extensions.AI (MEAI) speech clients over Amazon Transcribe and Amazon Polly, plus a `VoiceAgent` facade that will compose speech-to-text, an `IChatClient`, and text-to-speech into a full-duplex voice loop.

> **Preview scaffold.** This package is being built up over a stacked series of PRs. This branch ships only the package scaffold — no public APIs yet. The table below describes the **planned** surface; see the **Status** section for what has landed so far.

## What this package will provide

The planned surface depends on the target framework:

| Target framework | Planned surface |
| --- | --- |
| **net8.0** | Everything: Polly TTS, Transcribe STT, the full `VoiceAgent` loop, barge-in, and the Nova Sonic swap. |
| **net472, netstandard2.0** | The standalone Polly `ITextToSpeechClient` (`AsITextToSpeechClient()`) only. STT and the `VoiceAgent` loop are absent, because Amazon Transcribe's real-time API requires HTTP/2, which .NET Framework 4.7.2 lacks. |

## Status

This package is under active development. The speech clients (`TranscribeSpeechToTextClient`, `PollyTextToSpeechClient`) and the `VoiceAgent` facade land incrementally; see the design document for the delivery plan.

## License

Apache-2.0. See `LICENSE`.

# VoiceAgentWavSample

A console sample for [`AWS.Speech.MEAI`](../../src/AWS.Speech.MEAI/README.md). It reads a recorded utterance from a WAV file, runs it through the `VoiceAgent` loop (Amazon Transcribe for speech-to-text, Amazon Bedrock for reasoning, Amazon Polly for text-to-speech), prints the transcript and reply, and writes the spoken reply to an output WAV file.

## Prerequisites

- .NET 8 SDK or later.
- AWS credentials (default credential chain) with access to Amazon Transcribe, Amazon Bedrock, and Amazon Polly.
- A Bedrock model your account can invoke.
- An input WAV file: 16-bit signed little-endian mono PCM, 16 kHz recommended.

## Run

```bash
dotnet run -- input.wav reply.wav --model anthropic.claude-3-5-haiku-20241022-v1:0
```

Options:

- `--model <id>`: the Bedrock model for the reasoning step. Defaults to `anthropic.claude-3-5-haiku-20241022-v1:0`.
- `--instructions "<prompt>"`: an optional system prompt, for example `"You are a clinic intake assistant."`.

The sample writes the assistant's spoken reply to the output path as a 16 kHz mono PCM WAV file.

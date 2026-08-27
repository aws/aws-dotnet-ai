// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

// Voice-agent sample: read a recorded utterance from a WAV file, run it through the AWS.Speech.MEAI
// VoiceAgent loop (Amazon Transcribe -> Amazon Bedrock -> Amazon Polly), print the transcript and
// reply, and write the spoken reply to an output WAV file.
//
// Usage:
//   dotnet run -- <input.wav> <output.wav> [--model <bedrockModelId>] [--instructions "<system prompt>"]
//
// The input WAV must be 16-bit signed little-endian mono PCM (16 kHz recommended). Running the sample
// needs AWS credentials with access to Amazon Transcribe, Amazon Bedrock, and Amazon Polly, plus a
// Bedrock model that your account can invoke.

using AWS.Speech.MEAI;
using Microsoft.Extensions.AI;
using VoiceAgentWavSample;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: VoiceAgentWavSample <input.wav> <output.wav> [--model <id>] [--instructions \"<prompt>\"]");
    return 1;
}

string inputPath = args[0];
string outputPath = args[1];
string modelId = GetOption("--model") ?? "anthropic.claude-3-5-haiku-20241022-v1:0";
string? instructions = GetOption("--instructions");

var (inputPcm, inputSampleRate) = Wav.ReadPcm(inputPath);
Console.WriteLine($"Read {inputPcm.Length} bytes of {inputSampleRate} Hz PCM from {inputPath}.");

await using var agent = VoiceAgent.Create(options =>
{
    options.ModelId = modelId;
    options.InputSampleRateHertz = inputSampleRate;
    options.OutputSampleRateHertz = 16000;
    if (!string.IsNullOrEmpty(instructions))
    {
        options.Instructions = instructions;
    }
});

using var microphone = new MemoryStream(inputPcm);
using var spokenReply = new MemoryStream();

await foreach (var update in agent.RunAsync(microphone))
{
    switch (update.Kind)
    {
        case VoiceAgentUpdateKind.UserTranscriptFinal:
            Console.WriteLine($"caller: {update.Text}");
            break;
        case VoiceAgentUpdateKind.AssistantText when update.Text is { Length: > 0 }:
            Console.Write(update.Text);
            break;
        case VoiceAgentUpdateKind.AssistantAudio when update.Audio is { } pcm:
            spokenReply.Write(pcm.Span);
            break;
        case VoiceAgentUpdateKind.TurnComplete:
            Console.WriteLine();
            break;
    }
}

Wav.WritePcm(outputPath, spokenReply.ToArray(), sampleRateHertz: 16000);
Console.WriteLine($"Wrote {spokenReply.Length} bytes of spoken reply to {outputPath}.");
return 0;

string? GetOption(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

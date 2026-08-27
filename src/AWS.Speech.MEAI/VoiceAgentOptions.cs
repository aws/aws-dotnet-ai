// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Amazon;
using Amazon.Polly;
using Amazon.Runtime;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace AWS.Speech.MEAI;

/// <summary>Configuration for a <see cref="VoiceAgent"/>.</summary>
[Experimental("MEAI001")]
public sealed class VoiceAgentOptions
{
    /// <summary>The Amazon Bedrock model ID for the reasoning step (for example, an Anthropic Claude model).</summary>
    public string? ModelId { get; set; }

    /// <summary>The Amazon Polly voice to synthesize with. Defaults to <see cref="VoiceId.Matthew"/>.</summary>
    public VoiceId Voice { get; set; } = VoiceId.Matthew;

    /// <summary>The system prompt, applied at the start of the conversation history.</summary>
    public string? Instructions { get; set; }

    /// <summary>The Amazon Transcribe language code. Defaults to <c>en-US</c>.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Input PCM sample rate in hertz. Defaults to 16000.</summary>
    public int InputSampleRateHertz { get; set; } = 16000;

    /// <summary>Output PCM sample rate in hertz. Defaults to 16000; Amazon Polly PCM supports only 8000 or 16000.</summary>
    public int OutputSampleRateHertz { get; set; } = 16000;

    /// <summary>Enables barge-in (wired in a later phase).</summary>
    public bool EnableBargeIn { get; set; } = true;

    /// <summary>End-of-utterance debounce (wired with the barge-in tuning pass in a later phase).</summary>
    public int EndOfUtteranceSilenceMs { get; set; } = 700;

    /// <summary>Tools passed through to the reasoning <c>IChatClient</c> via <see cref="ChatOptions.Tools"/>.</summary>
    public IList<AITool>? Tools { get; set; }

    /// <summary>The AWS region for the constructed AWS clients. <see langword="null"/> uses the default region chain.</summary>
    public RegionEndpoint? Region { get; set; }

    /// <summary>The AWS credentials for the constructed AWS clients. <see langword="null"/> uses the default credential chain.</summary>
    public AWSCredentials? Credentials { get; set; }

    /// <summary>Which backend drives the agent. Defaults to <see cref="VoiceAgentBackend.Pipeline"/>.</summary>
    public VoiceAgentBackend Backend { get; set; } = VoiceAgentBackend.Pipeline;

    /// <summary>Pre-built speech-to-text client. When set, wins over the default AWS client chain.</summary>
    public ISpeechToTextClient? SpeechToTextClient { get; set; }

    /// <summary>Pre-built chat client. When set, wins over the default AWS client chain.</summary>
    public IChatClient? ChatClient { get; set; }

    /// <summary>Pre-built text-to-speech client. When set, wins over the default AWS client chain.</summary>
    public ITextToSpeechClient? TextToSpeechClient { get; set; }
}
#endif

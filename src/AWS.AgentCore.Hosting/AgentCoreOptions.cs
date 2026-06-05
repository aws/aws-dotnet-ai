// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AWS.AgentCore.Hosting;

/// <summary>
/// Options for configuring the AgentCore services.
/// </summary>
public class AgentCoreOptions
{
    /// <summary>
    /// The Bedrock model ID. When set (and <see cref="ChatClient"/> is null), registers a Bedrock-backed IChatClient.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// The port to listen on. AgentCore Runtime expects 8080. Default: 8080.
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// An IChatClient instance. Takes precedence over <see cref="ModelId"/> and pre-registered DI.
    /// Use this to provide OpenAI, Anthropic, Ollama, or any custom IChatClient.
    /// </summary>
    public IChatClient? ChatClient { get; set; }

    /// <summary>
    /// Options for the ChatClientAgent (tools, instructions, chat options).
    /// Passed directly to the ChatClientAgent constructor.
    /// </summary>
    public ChatClientAgentOptions? AgentOptions { get; set; }

    /// <summary>
    /// Optional callback to configure the agent after construction.
    /// Use <c>agent.AsBuilder().Use()</c> to add middleware.
    /// The callback receives the built ChatClientAgent and returns the configured AIAgent
    /// (which may be decorated with middleware).
    /// </summary>
    public Func<ChatClientAgent, AIAgent>? ConfigureAgent { get; set; }

    /// <summary>
    /// The AgentCore Memory ID for persistent conversation history.
    /// When set, the Memory provider actively loads and saves conversation history
    /// across invocations and container restarts.
    /// Falls back to the <see cref="Constants.MemoryIdEnvironmentVariable"/> environment variable when not set.
    /// When neither is configured, the agent operates statelessly.
    /// </summary>
    public string? MemoryId { get; set; }
}

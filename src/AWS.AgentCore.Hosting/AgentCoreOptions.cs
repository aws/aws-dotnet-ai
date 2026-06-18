// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

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

    /// <summary>
    /// When <c>true</c>, <c>AddAgentCore()</c> registers a default OpenTelemetry pipeline
    /// targeting the AgentCore Runtime OTLP sidecar (<c>http://localhost:4318</c>, HTTP/Protobuf)
    /// with ASP.NET Core, HttpClient, and AWS SDK instrumentation, plus an OTLP exporter for
    /// traces, metrics, and logs. Default: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this to <c>true</c> for standalone agents that do not bring their own
    /// OpenTelemetry setup.
    /// </para>
    /// <para>
    /// Leave this <c>false</c> when the application already configures its own OpenTelemetry
    /// pipeline (e.g. via Aspire <c>ServiceDefaults</c>, ADOT, or a custom OTel setup). In that
    /// case, the <c>IChatClient</c> and <c>AIAgent</c> wrappers still emit telemetry and the
    /// AgentCore activity sources/meters are subscribed via the
    /// <c>AddAgentCoreInstrumentation()</c> extensions on
    /// <see cref="TracerProviderBuilder"/> / <see cref="MeterProviderBuilder"/>.
    /// </para>
    /// </remarks>
    public bool EnableObservability { get; set; }

    /// <summary>
    /// When true, the OpenTelemetry instrumentation on the wrapped <c>IChatClient</c> and
    /// <c>AIAgent</c> will include sensitive data such as prompts, responses, function arguments,
    /// and function results in span attributes and metrics. Default: false.
    /// </summary>
    /// <remarks>
    /// Enable only in development and test environments. Sensitive data may include user input,
    /// model output, and tool invocation parameters that should not be exposed in production logs.
    /// </remarks>
    public bool EnableSensitiveTelemetryData { get; set; }

    /// <summary>
    /// Optional callback to customize the TracerProviderBuilder after defaults are applied.
    /// Use this to add custom activity sources, additional instrumentation, or samplers.
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// Optional callback to customize the MeterProviderBuilder after defaults are applied.
    /// Use this to add custom meters or views.
    /// </summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>
    /// Optional callback to customize the OpenTelemetry logging configuration after defaults are applied.
    /// </summary>
    public Action<OpenTelemetryLoggerOptions>? ConfigureLogging { get; set; }
}

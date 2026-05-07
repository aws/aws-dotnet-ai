// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

// This sample demonstrates the full extent of Microsoft Agent Framework configuration
// available through AWS.AgentCore, including:
// - Agent middleware (logging, intercepts every run)
// - Function-calling middleware (intercepts every tool invocation)
// - System instructions via ChatOptions
// - Multiple tools
// - ConfigureAgent callback for pipeline decoration

using System.ComponentModel;
using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkSample;
using MicrosoftAgentFrameworkSample.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";

    // Configure the agent with instructions and tools via standard MS AF options
    options.AgentOptions = new ChatClientAgentOptions
    {
        ChatOptions = new()
        {
            Instructions = "You are a helpful travel assistant. You can check weather, " +
                           "search for flights, and provide app information. " +
                           "Always be concise and friendly.",
            Tools =
            [
                AIFunctionFactory.Create(GetWeather),
                AIFunctionFactory.Create(SearchFlights),
                AIFunctionFactory.Create(GetAppInfo)
            ]
        }
    };

    // Decorate the agent with middleware using the standard MS AF builder pattern.
    // AsBuilder().Build() returns AIAgent (which may wrap the original ChatClientAgent).
    options.ConfigureAgent = agent => agent
        .AsBuilder()
        .Use(runFunc: AgentMiddleware.LoggingMiddleware, runStreamingFunc: null)
        .Use(AgentMiddleware.ToolExecutionMiddleware)
        .Build();
});

var app = builder.Build();

// The handler resolves AIAgent from DI (not ChatClientAgent, since middleware is applied).
// AIAgent is the base type that supports the full middleware pipeline.
app.MapAgentCore<PromptRequest>(async (
    PromptRequest request,
    AIAgent agent,
    AgentCoreRuntimeContext context,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    logger.LogInformation("Invocation started — SessionId={SessionId}, RequestId={RequestId}",
        context.SessionId, context.RequestId);

    var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
    var response = await agent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: cancellationToken);

    logger.LogInformation("Invocation complete — SessionId={SessionId}, RequestId={RequestId}",
        context.SessionId, context.RequestId);
    return response.ToString();
});

app.Run();

// ──────────────────────────────────────────────────────────────────────────────
// Tools
// ──────────────────────────────────────────────────────────────────────────────

[Description("Gets the current weather for a given location.")]
static string GetWeather([Description("The city or location to get weather for.")] string location)
    => $"The current weather in {location} is 72°F and sunny.";

[Description("Searches for available flights between two cities on a given date.")]
static string SearchFlights(
    [Description("The departure city.")] string from,
    [Description("The destination city.")] string to,
    [Description("The travel date in YYYY-MM-DD format.")] string date)
    => $"Found 3 flights from {from} to {to} on {date}: " +
       $"AA101 ($299, 8:00 AM), UA205 ($349, 11:30 AM), DL310 ($275, 3:15 PM).";

[Description("Returns runtime information about this application as a JSON string.")]
static string GetAppInfo()
{
    var isAot = typeof(object).Assembly.Location == string.Empty;
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown",
        isNativeAot = isAot,
        framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
    });
}

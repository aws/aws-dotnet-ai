// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using StreamingAgent.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";
    options.AgentOptions = new ChatClientAgentOptions
    {
        ChatOptions = new() { Tools = [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(GetAppInfo)] }
    };
});

var app = builder.Build();

app.MapAgentCore<PromptRequest>(
    (PromptRequest request, ChatClientAgent agent, AgentCoreRuntimeContext context,
        ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        logger.LogInformation("Streaming invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        return Stream(cancellationToken);

        async IAsyncEnumerable<string> Stream([EnumeratorCancellation] CancellationToken ct = default)
        {
            var session = await agent.CreateSessionAsync(cancellationToken: ct);

            await foreach (var update in agent.RunStreamingAsync(
                request.Prompt ?? "Hello!", session: session, cancellationToken: ct))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }

            logger.LogInformation("Streaming complete — SessionId={SessionId}, RequestId={RequestId}",
                context.SessionId, context.RequestId);
        }
    });

app.Run();

[Description("Gets the current weather for a given location.")]
static string GetWeather([Description("The city or location to get weather for.")] string location)
    => $"The current weather in {location} is 72°F and sunny.";

[Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
static string GetAppInfo()
{
    var isAot = typeof(object).Assembly.Location == string.Empty;
    return System.Text.Json.JsonSerializer.Serialize(new
    {
        appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown",
        isNativeAot = isAot,
        framework = RuntimeInformation.FrameworkDescription,
        architecture = RuntimeInformation.OSArchitecture.ToString(),
        os = RuntimeInformation.OSDescription
    });
}

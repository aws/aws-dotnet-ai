// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using AWS.AgentCore;
using AnnotationsSample.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AnnotationsSample;

public class Agent(IChatClient chatClient, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var agent = chatClient.AsAIAgent(tools: [AIFunctionFactory.Create(GetWeather), AIFunctionFactory.Create(GetAppInfo)]);
        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Gets the current weather for a given location.")]
    static string GetWeather([Description("The city or location to get weather for.")] string location)
        => $"The current weather in {location} is 72°F and sunny.";

    [Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
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
}

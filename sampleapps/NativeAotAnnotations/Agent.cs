// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using AWS.AgentCore.Hosting;
using Microsoft.Agents.AI;
using NativeAotAnnotations.Models;

namespace NativeAotAnnotations;

public class Agent(AIAgent chatAgent, ILogger<Agent> logger)
{
    [AgentCoreHandler(JsonContext = typeof(AppJsonContext))]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var session = await chatAgent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await chatAgent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public string Ping() => JsonSerializer.Serialize(
        new PingResponse("Healthy", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        AppJsonContext.Default.PingResponse);

    [Description("Gets the current weather for a given location.")]
    public static string GetWeather([Description("The city or location to get weather for.")] string location)
        => $"The current weather in {location} is 72°F and sunny.";

    [Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location returning empty is the intentional AOT detection mechanism.")]
    public static string GetAppInfo()
    {
        var info = new AppInfoResponse
        {
            AppName = "NativeAotAnnotations",
            IsNativeAot = typeof(object).Assembly.Location == string.Empty,
            Framework = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            Os = RuntimeInformation.OSDescription
        };
        return JsonSerializer.Serialize(info, AppJsonContext.Default.AppInfoResponse);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AWS.AgentCore;
using NativeAotAnnotations.Models;
using Microsoft.Extensions.AI;

namespace NativeAotAnnotations;

public class Agent(IChatClient chatClient, ILogger<Agent> logger)
{
    [AgentCoreHandler(JsonContext = typeof(AppJsonContext))]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        // Return app info directly without calling the LLM (deterministic, avoids Bedrock SDK AOT tool bug)
        if (request.Prompt?.Contains("GetAppInfo", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GetAppInfo();
        }

        var agent = chatClient.AsAIAgent();
        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public string Ping() => JsonSerializer.Serialize(
        new PingResponse("Healthy", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        AppJsonContext.Default.PingResponse);

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location returning empty is the intentional AOT detection mechanism.")]
    private static string GetAppInfo()
    {
        var info = new AppInfoResponse
        {
            AppName = "NativeAotAnnotations",
            IsNativeAot = typeof(object).Assembly.Location == string.Empty,
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
        };
        return JsonSerializer.Serialize(info, AppJsonContext.Default.AppInfoResponse);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore;
using NativeAotAnnotations.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

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

        var agent = chatClient.AsAIAgent();
        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
}

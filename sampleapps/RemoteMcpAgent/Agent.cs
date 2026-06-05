// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting;
using RemoteMcpAgent.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RemoteMcpAgent;

public class Agent(ChatClientAgent chatAgent, McpToolProvider mcpToolProvider, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        await mcpToolProvider.EnsureConnectedAsync(cancellationToken);

        var session = await chatAgent.CreateSessionAsync(cancellationToken: cancellationToken);

        var runOptions = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions
            {
                Tools = [..mcpToolProvider.Tools]
            }
        };

        var response = await chatAgent.RunAsync(
            request.Prompt ?? "Hello!", session: session, options: runOptions, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
}

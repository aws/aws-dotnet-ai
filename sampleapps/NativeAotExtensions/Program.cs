// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using Microsoft.Extensions.AI;
using NativeAotExtensions.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-sonnet-4-20250514-v1:0";
});

var app = builder.Build();

// NativeAOT-safe: strongly-typed handler with IServiceProvider for DI access
app.MapAgentCore<PromptRequest>(
    async (request, context, services, ct) =>
    {
        var chatClient = services.GetRequiredService<IChatClient>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var agent = chatClient.AsAIAgent();
        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: ct);

        return response.ToString();
    },
    AppJsonContext.Default.PromptRequest);

app.Run();

[JsonSerializable(typeof(PromptRequest))]
internal partial class AppJsonContext : JsonSerializerContext;

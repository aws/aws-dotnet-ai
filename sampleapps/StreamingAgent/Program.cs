// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Runtime.CompilerServices;
using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using Microsoft.Extensions.AI;
using StreamingAgent.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-sonnet-4-20250514-v1:0";
});

var app = builder.Build();

app.MapAgentCore<PromptRequest>(
    (PromptRequest request, AgentCoreRuntimeContext context, IChatClient chatClient,
        ILogger<Program> logger, CancellationToken cancellationToken) =>
    {
        logger.LogInformation("Streaming invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        return Stream();

        async IAsyncEnumerable<string> Stream([EnumeratorCancellation] CancellationToken ct = default)
        {
            var agent = chatClient.AsAIAgent(tools: [AIFunctionFactory.Create(GetWeather)]);
            var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);

            await foreach (var update in agent.RunStreamingAsync(
                request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken))
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

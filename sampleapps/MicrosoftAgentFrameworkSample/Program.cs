// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using AWS.AgentCore.Extensions;
using Microsoft.Extensions.AI;
using MicrosoftAgentFrameworkSample.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-sonnet-4-20250514-v1:0";
});

var app = builder.Build();

app.MapAgentCore<PromptRequest>(async (request, chatClient, cancellationToken) =>
{
    var agent = chatClient.AsAIAgent(tools: [AIFunctionFactory.Create(GetWeather)]);
    var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
    var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken);
    return response.ToString();
});

app.Run();

[Description("Gets the current weather for a given location.")]
static string GetWeather([Description("The city or location to get weather for.")] string location)
    => $"The current weather in {location} is 72°F and sunny.";

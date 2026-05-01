// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using AWS.AgentCore.Extensions;
using Microsoft.Extensions.AI;
using NativeAotExtensions.Models;

var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";
});

var app = builder.Build();

// NativeAOT-safe: strongly-typed handler with IServiceProvider for DI access
app.MapAgentCore<PromptRequest>(
    async (request, context, services, ct) =>
    {
        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        // Return app info directly without calling the LLM (deterministic, avoids Bedrock SDK AOT tool bug)
        if (request.Prompt?.Contains("GetAppInfo", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GetAppInfo();
        }

        var chatClient = services.GetRequiredService<IChatClient>();
        var agent = chatClient.AsAIAgent();
        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: ct);

        return response.ToString();
    },
    AppJsonContext.Default.PromptRequest);

app.Run();

[UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location returning empty is the intentional AOT detection mechanism.")]
static string GetAppInfo()
{
    var info = new AppInfoResponse
    {
        AppName = "NativeAotExtensions",
        IsNativeAot = typeof(object).Assembly.Location == string.Empty,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
        Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription
    };
    return JsonSerializer.Serialize(info, AppJsonContext.Default.AppInfoResponse);
}

[JsonSerializable(typeof(PromptRequest))]
[JsonSerializable(typeof(AppInfoResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext;

internal record AppInfoResponse
{
    public string AppName { get; init; } = "";
    public bool IsNativeAot { get; init; }
    public string Framework { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Os { get; init; } = "";
}

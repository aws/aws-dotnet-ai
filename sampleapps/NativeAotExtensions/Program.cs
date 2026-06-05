// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NativeAotExtensions.Models;

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

// NativeAOT-safe: strongly-typed handler with IServiceProvider for DI access
app.MapAgentCore<PromptRequest>(
    async (request, context, services, ct) =>
    {
        var agent = services.GetRequiredService<ChatClientAgent>();
        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: ct);

        return response.ToString();
    },
    AppJsonContext.Default);

app.Run();

[Description("Gets the current weather for a given location.")]
static string GetWeather([Description("The city or location to get weather for.")] string location)
    => $"The current weather in {location} is 72°F and sunny.";

[Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
[UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location returning empty is the intentional AOT detection mechanism.")]
static string GetAppInfo()
{
    var info = new AppInfoResponse
    {
        AppName = "NativeAotExtensions",
        IsNativeAot = typeof(object).Assembly.Location == string.Empty,
        Framework = RuntimeInformation.FrameworkDescription,
        Architecture = RuntimeInformation.OSArchitecture.ToString(),
        Os = RuntimeInformation.OSDescription
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

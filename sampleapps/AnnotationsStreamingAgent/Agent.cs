// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using AWS.AgentCore.Hosting;
using AnnotationsStreamingAgent.Models;
using Microsoft.Agents.AI;

namespace AnnotationsStreamingAgent;

public class Agent(AIAgent chatAgent, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public IAsyncEnumerable<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Streaming invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        return Stream(cancellationToken);

        async IAsyncEnumerable<string> Stream([EnumeratorCancellation] CancellationToken ct = default)
        {
            var session = await chatAgent.CreateSessionAsync(cancellationToken: ct);

            await foreach (var update in chatAgent.RunStreamingAsync(
                request.Prompt ?? "Hello!", session: session, cancellationToken: ct))
            {
                var text = update.Text;
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }

            logger.LogInformation("Streaming complete — SessionId={SessionId}, RequestId={RequestId}",
                context.SessionId, context.RequestId);
        }
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Gets the current weather for a given location.")]
    public static string GetWeather([Description("The city or location to get weather for.")] string location)
        => $"The current weather in {location} is 72°F and sunny.";

    [Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
    public static string GetAppInfo()
    {
        var isAot = typeof(object).Assembly.Location == string.Empty;
        return JsonSerializer.Serialize(new
        {
            appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "Unknown",
            isNativeAot = isAot,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            os = RuntimeInformation.OSDescription
        });
    }
}

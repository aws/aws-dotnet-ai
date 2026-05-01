// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Amazon.S3;
using AWS.AgentCore;
using AnnotationsSample.Models;
using Microsoft.Extensions.AI;

namespace AnnotationsSample;

public class Agent(IChatClient chatClient, IAmazonS3 s3Client, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var agent = chatClient.AsAIAgent(tools:
        [
            AIFunctionFactory.Create(GetWeather),
            AIFunctionFactory.Create(GetAppInfo),
            AIFunctionFactory.Create(GetS3BucketCount)
        ]);
        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Gets the current weather for a given location.")]
    static string GetWeather([Description("The city or location to get weather for.")] string location)
        => $"The current weather in {location} is 72°F and sunny.";

    [Description("Returns runtime information about this application as a JSON string. Call this when asked about the app's name, architecture, framework, or whether it is running as NativeAOT. Return the JSON result directly to the user without modification.")]
    static string GetAppInfo()
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

    [Description("Returns the number of S3 buckets in the AWS account. Use this when asked about S3 buckets or AWS resources. This uses the AWS SDK credential chain to authenticate.")]
    async Task<string> GetS3BucketCount()
    {
        try
        {
            var response = await s3Client.ListBucketsAsync();
            return JsonSerializer.Serialize(new
            {
                bucketCount = response.Buckets.Count,
                bucketNames = response.Buckets.Select(b => b.BucketName).ToList()
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = ex.GetType().Name,
                message = ex.Message,
                innerError = ex.InnerException?.Message
            });
        }
    }
}

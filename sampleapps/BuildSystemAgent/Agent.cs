// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Text.Json;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.CodeBuild;
using Amazon.CodeBuild.Model;
using AWS.AgentCore;
using BuildSystemAgent.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BuildSystemAgent;

public class Agent(
    IChatClient chatClient,
    IAmazonCodeBuild codeBuild,
    IAmazonCloudWatchLogs cloudWatchLogs,
    ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Build agent invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        const string systemPrompt = """
            You are a build system agent that manages AWS CodeBuild builds.
            You can start builds, check build status, and retrieve build logs.

            The caller's prompt includes the CodeBuild project name and PR context.
            Use the project name from the prompt when starting builds.
            When starting a build from a PR, use the PR branch name as the sourceVersion.
            Always confirm what you're about to do before starting a build.
            After starting a build, report the build ID so the user can track it.
            """;

        var agent = chatClient.AsAIAgent(tools:
        [
            AIFunctionFactory.Create(StartBuild),
            AIFunctionFactory.Create(GetBuildStatus),
            AIFunctionFactory.Create(GetBuildLogs),
        ]);

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var fullPrompt = $"[System: {systemPrompt}]\n\nUser: {request.Prompt ?? "Hello!"}";
        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Starts an AWS CodeBuild build for a given project and source version (branch, tag, or commit SHA). Returns the build ID.")]
    async Task<string> StartBuild(
        [Description("The CodeBuild project name.")] string projectName,
        [Description("The source version to build: a branch name (e.g. 'feature/my-change'), a PR ref (e.g. 'pr/42'), or a commit SHA.")] string sourceVersion)
    {
        try
        {
            var response = await codeBuild.StartBuildAsync(new StartBuildRequest
            {
                ProjectName = projectName,
                SourceVersion = sourceVersion,
            });

            return JsonSerializer.Serialize(new
            {
                buildId = response.Build.Id,
                buildNumber = response.Build.BuildNumber,
                projectName = response.Build.ProjectName,
                sourceVersion = response.Build.SourceVersion,
                buildStatus = response.Build.BuildStatus.Value,
                startTime = response.Build.StartTime?.ToString("u"),
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Gets the current status of a CodeBuild build by its build ID. Returns status, phase, start/end times, and whether it succeeded.")]
    async Task<string> GetBuildStatus(
        [Description("The CodeBuild build ID (e.g. 'my-project:abc123-def456').")] string buildId)
    {
        try
        {
            var response = await codeBuild.BatchGetBuildsAsync(new BatchGetBuildsRequest
            {
                Ids = [buildId],
            });

            if (response.Builds.Count == 0)
                return JsonSerializer.Serialize(new { error = $"Build '{buildId}' not found." });

            var build = response.Builds[0];
            return JsonSerializer.Serialize(new
            {
                buildId = build.Id,
                buildStatus = build.BuildStatus.Value,
                currentPhase = build.CurrentPhase,
                startTime = build.StartTime?.ToString("u"),
                endTime = build.EndTime?.ToString("u"),
                sourceVersion = build.SourceVersion,
                durationSeconds = build.EndTime.HasValue && build.StartTime.HasValue
                    ? (build.EndTime.Value - build.StartTime.Value).TotalSeconds
                    : build.StartTime.HasValue
                        ? (DateTimeOffset.UtcNow - build.StartTime.Value).TotalSeconds
                        : (double?)null,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Fetches the last N lines of build logs from CloudWatch for a CodeBuild build. Useful for checking build output or errors.")]
    async Task<string> GetBuildLogs(
        [Description("The CodeBuild build ID.")] string buildId,
        [Description("Number of log lines to retrieve (default 50, max 200).")] int? lines)
    {
        try
        {
            var lineCount = Math.Min(lines ?? 50, 200);

            var buildResponse = await codeBuild.BatchGetBuildsAsync(new BatchGetBuildsRequest
            {
                Ids = [buildId],
            });

            if (buildResponse.Builds.Count == 0)
                return JsonSerializer.Serialize(new { error = $"Build '{buildId}' not found." });

            var build = buildResponse.Builds[0];
            var logGroup = build.Logs?.GroupName;
            var logStream = build.Logs?.StreamName;

            if (string.IsNullOrEmpty(logGroup) || string.IsNullOrEmpty(logStream))
                return JsonSerializer.Serialize(new { error = "Build logs are not available yet.", buildStatus = build.BuildStatus.Value });

            var logResponse = await cloudWatchLogs.GetLogEventsAsync(new GetLogEventsRequest
            {
                LogGroupName = logGroup,
                LogStreamName = logStream,
                Limit = lineCount,
                StartFromHead = false,
            });

            var logLines = logResponse.Events
                .Select(e => e.Message?.TrimEnd())
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList();

            return JsonSerializer.Serialize(new
            {
                buildId,
                buildStatus = build.BuildStatus.Value,
                logLineCount = logLines.Count,
                logs = logLines,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }
}

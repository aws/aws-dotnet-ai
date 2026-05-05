// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.CodeBuild;
using Amazon.CodeBuild.Model;
using AWS.AgentCore;
using BuildDiagnosticAgent.Heuristics;
using BuildDiagnosticAgent.Models;
using BuildDiagnosticAgent.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BuildDiagnosticAgent;

public class Agent(
    IChatClient chatClient,
    IAmazonCodeBuild codeBuild,
    IAmazonCloudWatchLogs cloudWatchLogs,
    GitHubClient gitHubClient,
    ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Diagnostic agent invocation - SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        var systemPrompt = $$"""
            You are a build-failure diagnostician for the aws/aws-dotnet-ai repository.
            When a CodeBuild build fails, your job is to identify what failed, why, and
            what the PR author should do about it. Output is a structured Markdown
            comment posted back to the PR.

            Your tools focus on the build side. PR diff and file-content reads live in
            the sister BuildSystemAgent (triggered via /build); you can reference them
            in your response but cannot call them directly.

            Workflow:
            1. If the prompt names a CodeBuild project and a branch but no build_id,
               call ListRecentBuilds to resolve the most recent failed build.
            2. Call GetFailedTests(build_id) to learn what failed.
            3. If the surface logs aren't enough, call GetBuildLogs(build_id) for more
               context around the failure.
            4. If the failed test name is one that might be flaky, call
               SearchPastFailures to look for prior occurrences.
            5. If a stack trace points at a specific file, call GitBlame on that file
               to learn which recent commit might have introduced the regression.

            {{CodebaseHeuristics.Patterns}}

            Output format (Markdown):
              ### 🔬 Diagnosis for build {build_id}
              **Failed test(s)**: {names}
              **Root cause hypothesis**: {1-3 sentences}
              **Evidence**:
              - {bullet} (link to source line, blame, similar issue)
              **Suggested fix**: {1-3 sentences}
              **Confidence**: {high|medium|low} ({why})

            If a deeper look at the PR diff or specific source file would unlock the
            diagnosis, end with: "Run `/build show me the diff for <file>` for more
            context." This pulls BuildSystemAgent in for the file-side reads.

            If you can't determine a root cause from available evidence, say so plainly.
            Don't speculate beyond what the tools surface.
            """;

        var agent = chatClient.AsAIAgent(tools:
        [
            AIFunctionFactory.Create(GetBuildLogs),
            AIFunctionFactory.Create(GetFailedTests),
            AIFunctionFactory.Create(SearchPastFailures),
            AIFunctionFactory.Create(GitBlame),
            AIFunctionFactory.Create(ListRecentBuilds),
        ]);

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var fullPrompt = $"[System: {systemPrompt}]\n\nUser: {request.Prompt ?? "Diagnose the most recent failed build on this PR."}";
        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    [Description("Fetches the last N lines of build logs from CloudWatch for a CodeBuild build. Useful for inspecting build output around a failure.")]
    async Task<string> GetBuildLogs(
        [Description("The CodeBuild build ID (e.g. 'my-project:abc123-def456').")] string buildId,
        [Description("Number of log lines to retrieve (default 100, max 500).")] int? lines)
    {
        try
        {
            var lineCount = Math.Min(lines ?? 100, 500);

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

    [Description("Parses CodeBuild logs for failed test names, assertions, and stack-trace tops. Returns up to 10 failures. Prefer this over raw logs when diagnosing test failures.")]
    async Task<string> GetFailedTests(
        [Description("The CodeBuild build ID.")] string buildId)
    {
        try
        {
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
                return JsonSerializer.Serialize(new { error = "Build logs not available.", buildStatus = build.BuildStatus.Value });

            var logResponse = await cloudWatchLogs.GetLogEventsAsync(new GetLogEventsRequest
            {
                LogGroupName = logGroup,
                LogStreamName = logStream,
                Limit = 5000,
                StartFromHead = true,
            });

            var lines = logResponse.Events.Select(e => e.Message ?? "").ToList();
            var failures = ParseFailures(lines).Take(10).ToList();

            return JsonSerializer.Serialize(new
            {
                buildId,
                buildStatus = build.BuildStatus.Value,
                failureCount = failures.Count,
                failures,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Searches recent issues and PRs in the repo for occurrences of a test name. Useful for distinguishing flaky tests from new regressions.")]
    async Task<string> SearchPastFailures(
        [Description("The test name or unique substring to search for.")] string testName,
        [Description("GitHub repository owner (org or user).")] string owner,
        [Description("GitHub repository name.")] string repo,
        [Description("Look-back window in days (default 30, max 365).")] int? days)
    {
        try
        {
            var window = Math.Min(days ?? 30, 365);
            var since = DateTimeOffset.UtcNow.AddDays(-window).ToString("yyyy-MM-dd");
            var query = $"\"{testName}\" repo:{owner}/{repo} created:>{since}";

            var json = await gitHubClient.SearchIssuesAsync(query);
            var root = JsonDocument.Parse(json).RootElement;

            var matches = root.GetProperty("items").EnumerateArray().Take(10).Select(i => new
            {
                number = i.GetProperty("number").GetInt32(),
                title = i.GetProperty("title").GetString(),
                url = i.GetProperty("html_url").GetString(),
                state = i.GetProperty("state").GetString(),
                createdAt = i.GetProperty("created_at").GetString(),
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                testName,
                lookbackDays = window,
                totalMatches = root.GetProperty("total_count").GetInt32(),
                shown = matches.Count,
                matches,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Returns the recent commit history (last 3) that touched a file. Approximation of git blame; surfaces who recently modified the file rather than line-by-line attribution.")]
    async Task<string> GitBlame(
        [Description("GitHub repository owner (org or user).")] string owner,
        [Description("GitHub repository name.")] string repo,
        [Description("File path relative to repo root.")] string file,
        [Description("Optional 1-based line number (currently advisory only; commits API doesn't filter by line).")] int? line,
        [Description("Git ref (branch name, tag, or commit SHA) to start the history from.")] string @ref)
    {
        try
        {
            var json = await gitHubClient.GetCommitsForPathAsync(owner, repo, file, @ref);
            var commits = JsonDocument.Parse(json).RootElement;

            var top = commits.EnumerateArray().Take(3).Select(c =>
            {
                var sha = c.GetProperty("sha").GetString() ?? "";
                var commit = c.GetProperty("commit");
                var author = commit.GetProperty("author");
                var message = commit.GetProperty("message").GetString() ?? "";
                return new
                {
                    sha = sha[..Math.Min(7, sha.Length)],
                    author = author.GetProperty("name").GetString(),
                    date = author.GetProperty("date").GetString(),
                    message = message.Split('\n', 2)[0],
                };
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                file,
                line,
                @ref,
                recentCommits = top,
                note = "File-level commit history. Line-level blame requires the GraphQL API and is not implemented in this hackathon scope.",
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Lists recent CodeBuild builds for a project, optionally filtered by source version (branch name or commit SHA). Use this when you need to resolve a build_id from a branch.")]
    async Task<string> ListRecentBuilds(
        [Description("CodeBuild project name.")] string projectName,
        [Description("Optional source version filter: branch name or commit SHA.")] string? sourceVersion,
        [Description("Maximum number of builds to return (default 5, max 20).")] int? limit)
    {
        try
        {
            var max = Math.Min(limit ?? 5, 20);

            var idsResponse = await codeBuild.ListBuildsForProjectAsync(new ListBuildsForProjectRequest
            {
                ProjectName = projectName,
                SortOrder = SortOrderType.DESCENDING,
            });

            if (idsResponse.Ids.Count == 0)
                return JsonSerializer.Serialize(new { projectName, builds = Array.Empty<object>() });

            var idsToFetch = idsResponse.Ids.Take(20).ToList();
            var detailResponse = await codeBuild.BatchGetBuildsAsync(new BatchGetBuildsRequest { Ids = idsToFetch });

            var builds = detailResponse.Builds
                .Where(b => sourceVersion is null || b.SourceVersion == sourceVersion || b.ResolvedSourceVersion == sourceVersion)
                .Take(max)
                .Select(b => new
                {
                    buildId = b.Id,
                    buildStatus = b.BuildStatus.Value,
                    sourceVersion = b.SourceVersion,
                    resolvedSourceVersion = b.ResolvedSourceVersion,
                    startTime = b.StartTime?.ToString("u"),
                    endTime = b.EndTime?.ToString("u"),
                })
                .ToList();

            return JsonSerializer.Serialize(new { projectName, sourceVersionFilter = sourceVersion, count = builds.Count, builds });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    private static IEnumerable<object> ParseFailures(IList<string> lines)
    {
        var xunitFailureRegex = new Regex(@"\[FAIL\]\s+(?<name>[^\s]+)|^\s*Failed\s+(?<name2>[\w\.]+)", RegexOptions.Compiled);
        var assertionRegex = new Regex(@"(Assert\.\w+\(\)\s+Failure|Expected:|Actual:)", RegexOptions.Compiled);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var match = xunitFailureRegex.Match(line);
            if (!match.Success) continue;

            var name = match.Groups["name"].Value;
            if (string.IsNullOrEmpty(name)) name = match.Groups["name2"].Value;
            if (string.IsNullOrEmpty(name)) continue;

            var contextStart = Math.Max(0, i - 2);
            var contextEnd = Math.Min(lines.Count, i + 8);
            var context = lines.Skip(contextStart).Take(contextEnd - contextStart).ToList();

            var assertionLine = context.FirstOrDefault(l => assertionRegex.IsMatch(l));
            var stackTraceTop = context.FirstOrDefault(l => l.Contains(" at ", StringComparison.Ordinal) && l.Contains(":line", StringComparison.Ordinal));

            yield return new
            {
                test_name = name,
                assertion = assertionLine?.Trim(),
                stack_trace_top = stackTraceTop?.Trim(),
                context_lines = context.Select(l => l.TrimEnd()).ToList(),
            };
        }
    }
}

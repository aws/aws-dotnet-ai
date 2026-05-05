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
using BuildSystemAgent.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BuildSystemAgent;

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
        logger.LogInformation("Build agent invocation — SessionId={SessionId}, RequestId={RequestId}",
            context.SessionId, context.RequestId);

        const string systemPrompt = """
            You are a build and code review agent for GitHub pull requests.

            You have two sets of capabilities:
            1. **Build management** — Start CodeBuild builds, check status, and fetch logs.
            2. **PR review** — Read PR diffs, file contents, CI check results, and submit inline review comments.

            The caller's prompt includes the GitHub repository (owner/repo), PR number, branch, and CodeBuild project name.

            When reviewing a PR:
            - First get the PR description and file list to understand the scope.
            - Then get the diff to see exactly what changed.
            - If you need more context around a change, fetch the full file content.
            - Identify bugs, security issues, performance problems, and style inconsistencies.
            - Use SubmitPRReview to post inline comments on specific problematic lines.
              The 'line' field must be the line number as it appears in the diff (the new file line number for additions/modifications).
            - After submitting the review, return a brief summary of your findings.
            - Be concise and actionable. Don't comment on things that are fine — only flag real issues.
            - Do NOT approve or request changes. Your review status will always be "Commented".

            When managing builds:
            - Use the CodeBuild project name from the prompt.
            - Use the PR branch as the sourceVersion.
            - Report the build ID after starting.

            Your response will be posted as a GitHub PR comment, so format it in markdown.
            """;

        var agent = chatClient.AsAIAgent(tools:
        [
            // Build tools
            AIFunctionFactory.Create(StartBuild),
            AIFunctionFactory.Create(GetBuildStatus),
            AIFunctionFactory.Create(GetBuildLogs),
            // PR review tools
            AIFunctionFactory.Create(GetPRDescription),
            AIFunctionFactory.Create(GetPRDiff),
            AIFunctionFactory.Create(GetPRFiles),
            AIFunctionFactory.Create(GetFileContent),
            AIFunctionFactory.Create(GetPRChecks),
            AIFunctionFactory.Create(SubmitPRReview),
        ]);

        var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
        var fullPrompt = $"[System: {systemPrompt}]\n\nUser: {request.Prompt ?? "Hello!"}";
        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);

        return response.ToString();
    }

    [AgentCorePing]
    public object Ping() => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };

    // ──────────────────────────────────────────────────────────────────
    // PR Review Tools
    // ──────────────────────────────────────────────────────────────────

    [Description("Gets the pull request metadata including title, description body, labels, and base/head branches.")]
    async Task<string> GetPRDescription(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The pull request number.")] int prNumber)
    {
        try
        {
            var json = await gitHubClient.GetPullRequestAsync(owner, repo, prNumber);
            var pr = JsonDocument.Parse(json).RootElement;

            return JsonSerializer.Serialize(new
            {
                title = pr.GetProperty("title").GetString(),
                body = pr.GetProperty("body").GetString(),
                state = pr.GetProperty("state").GetString(),
                baseBranch = pr.GetProperty("base").GetProperty("ref").GetString(),
                headBranch = pr.GetProperty("head").GetProperty("ref").GetString(),
                headSha = pr.GetProperty("head").GetProperty("sha").GetString(),
                additions = pr.GetProperty("additions").GetInt32(),
                deletions = pr.GetProperty("deletions").GetInt32(),
                changedFiles = pr.GetProperty("changed_files").GetInt32(),
                labels = pr.GetProperty("labels").EnumerateArray()
                    .Select(l => l.GetProperty("name").GetString()).ToList(),
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Gets the unified diff of all changes in the pull request. Shows exactly what lines were added, removed, or modified.")]
    async Task<string> GetPRDiff(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The pull request number.")] int prNumber)
    {
        try
        {
            var diff = await gitHubClient.GetPullRequestDiffAsync(owner, repo, prNumber);

            // Truncate very large diffs to avoid blowing up the context
            const int maxLength = 100_000;
            if (diff.Length > maxLength)
            {
                diff = diff[..maxLength] + "\n\n... [diff truncated — use GetFileContent for specific files]";
            }

            return diff;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Lists all files changed in the pull request with their status (added, modified, removed) and line change counts.")]
    async Task<string> GetPRFiles(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The pull request number.")] int prNumber)
    {
        try
        {
            var json = await gitHubClient.GetPullRequestFilesAsync(owner, repo, prNumber);
            var files = JsonDocument.Parse(json).RootElement;

            var summary = files.EnumerateArray().Select(f => new
            {
                filename = f.GetProperty("filename").GetString(),
                status = f.GetProperty("status").GetString(),
                additions = f.GetProperty("additions").GetInt32(),
                deletions = f.GetProperty("deletions").GetInt32(),
                changes = f.GetProperty("changes").GetInt32(),
            }).ToList();

            return JsonSerializer.Serialize(new { fileCount = summary.Count, files = summary });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Gets the full content of a file at the PR's head commit. Useful for understanding context around changes shown in the diff.")]
    async Task<string> GetFileContent(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The file path relative to the repository root.")] string filePath,
        [Description("The git ref to read the file at (branch name or commit SHA). Use the PR's head SHA or branch.")] string gitRef)
    {
        try
        {
            var content = await gitHubClient.GetFileContentAsync(owner, repo, filePath, gitRef);

            // Truncate very large files
            const int maxLength = 50_000;
            if (content.Length > maxLength)
            {
                content = content[..maxLength] + "\n\n... [file truncated]";
            }

            return content;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Gets CI check run results for the PR's head commit. Shows which checks passed, failed, or are still running.")]
    async Task<string> GetPRChecks(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The commit SHA to get check runs for (use the PR's head SHA).")] string commitSha)
    {
        try
        {
            var json = await gitHubClient.GetCheckRunsAsync(owner, repo, commitSha);
            var root = JsonDocument.Parse(json).RootElement;

            var checkRuns = root.GetProperty("check_runs").EnumerateArray().Select(c => new
            {
                name = c.GetProperty("name").GetString(),
                status = c.GetProperty("status").GetString(),
                conclusion = c.TryGetProperty("conclusion", out var conc) ? conc.GetString() : null,
                startedAt = c.TryGetProperty("started_at", out var sa) ? sa.GetString() : null,
                completedAt = c.TryGetProperty("completed_at", out var ca) ? ca.GetString() : null,
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                totalCount = root.GetProperty("total_count").GetInt32(),
                checkRuns,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    [Description("Submits a PR review with inline comments on specific lines of code. The review appears as 'Commented' (not approved or changes requested). Use this after analyzing the diff to leave feedback on problematic lines. All comments are posted as a single grouped review.")]
    async Task<string> SubmitPRReview(
        [Description("The GitHub repository owner (org or user).")] string owner,
        [Description("The GitHub repository name.")] string repo,
        [Description("The pull request number.")] int prNumber,
        [Description("The head commit SHA of the PR (ensures comments are anchored to the correct version).")] string commitSha,
        [Description("A summary body for the review (appears as the top-level review comment). Use actual newlines for formatting, not escaped \\n.")] string summary,
        [Description("JSON array of inline comments. Each object must have: 'path' (file path), 'line' (line number in the diff), 'body' (comment text). Example: [{\"path\":\"src/Foo.cs\",\"line\":42,\"body\":\"Potential null reference here.\"}]")] string commentsJson)
    {
        try
        {
            // Unescape literal \n sequences that the LLM may produce
            summary = summary.Replace("\\n", "\n");

            var comments = JsonSerializer.Deserialize<List<ReviewCommentInput>>(commentsJson)
                ?? throw new ArgumentException("Failed to parse comments JSON.");

            var reviewComments = comments.Select(c =>
                new GitHubClient.ReviewComment(c.Path, c.Line, c.Body.Replace("\\n", "\n"))).ToList();

            var result = await gitHubClient.SubmitPullRequestReviewAsync(
                owner, repo, prNumber, commitSha, summary, reviewComments);

            return JsonSerializer.Serialize(new
            {
                success = true,
                commentCount = reviewComments.Count,
                message = "Review submitted successfully with inline comments.",
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.GetType().Name, message = ex.Message });
        }
    }

    private record ReviewCommentInput(string Path, int Line, string Body);

    // ──────────────────────────────────────────────────────────────────
    // Build Tools
    // ──────────────────────────────────────────────────────────────────

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

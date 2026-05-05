// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;

namespace BuildSystemAgent.Services;

/// <summary>
/// Handles GitHub API calls using a token fetched from AWS Secrets Manager.
/// The token is never exposed outside this class — tools receive only API response data.
/// </summary>
public sealed class GitHubClient
{
    private const string SecretId = "prod/devex/private-github-repo-access-token";
    private const string SecretKey = "token";
    private const string GitHubApiBase = "https://api.github.com";

    private readonly IAmazonSecretsManager _secretsManager;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedToken;

    public GitHubClient(IAmazonSecretsManager secretsManager, HttpClient httpClient, ILogger<GitHubClient> logger)
    {
        _secretsManager = secretsManager;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the unified diff for a pull request.
    /// </summary>
    public async Task<string> GetPullRequestDiffAsync(string owner, string repo, int prNumber, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubApiBase}/repos/{owner}/{repo}/pulls/{prNumber}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.diff"));

        var response = await SendAuthenticatedAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Gets the list of files changed in a pull request.
    /// </summary>
    public async Task<string> GetPullRequestFilesAsync(string owner, string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await GetAsync($"/repos/{owner}/{repo}/pulls/{prNumber}/files?per_page=100", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Gets pull request metadata (title, body, labels, etc.).
    /// </summary>
    public async Task<string> GetPullRequestAsync(string owner, string repo, int prNumber, CancellationToken ct = default)
    {
        var response = await GetAsync($"/repos/{owner}/{repo}/pulls/{prNumber}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Gets the content of a file at a specific ref (branch or SHA).
    /// </summary>
    public async Task<string> GetFileContentAsync(string owner, string repo, string path, string gitRef, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{GitHubApiBase}/repos/{owner}/{repo}/contents/{path}?ref={gitRef}");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));

        var response = await SendAuthenticatedAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Gets check runs for a specific commit SHA.
    /// </summary>
    public async Task<string> GetCheckRunsAsync(string owner, string repo, string commitSha, CancellationToken ct = default)
    {
        var response = await GetAsync($"/repos/{owner}/{repo}/commits/{commitSha}/check-runs?per_page=50", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Submits a PR review with inline comments. The review event is always "COMMENT"
    /// (no approve/request-changes). Comments appear grouped as a single review.
    /// </summary>
    public async Task<string> SubmitPullRequestReviewAsync(
        string owner, string repo, int prNumber, string commitSha,
        string body, IReadOnlyList<ReviewComment> comments, CancellationToken ct = default)
    {
        var payload = new
        {
            commit_id = commitSha,
            body,
            @event = "COMMENT",
            comments = comments.Select(c => new
            {
                path = c.Path,
                line = c.Line,
                side = c.Side ?? "RIGHT",
                body = c.Body,
            }).ToArray(),
        };

        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{GitHubApiBase}/repos/{owner}/{repo}/pulls/{prNumber}/reviews")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await SendAuthenticatedAsync(request, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Represents a single inline review comment on a specific line of a file.</summary>
    public record ReviewComment(string Path, int Line, string Body, string? Side = "RIGHT");

    private async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubApiBase}{path}");
        return await SendAuthenticatedAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, ct);

        // If we get a 401, refresh the token and retry once
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("GitHub API returned 401 — refreshing token");
            _cachedToken = null;
            token = await GetTokenAsync(ct);

            // Rebuild the request (can't resend the same HttpRequestMessage)
            var retryRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    retryRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await _httpClient.SendAsync(retryRequest, ct);
        }

        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null)
                return _cachedToken;

            _logger.LogInformation("Fetching GitHub token from Secrets Manager");

            var response = await _secretsManager.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = SecretId,
            }, ct);

            var secretJson = JsonDocument.Parse(response.SecretString);
            _cachedToken = secretJson.RootElement.GetProperty(SecretKey).GetString()
                ?? throw new InvalidOperationException($"Secret '{SecretId}' does not contain key '{SecretKey}'.");

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

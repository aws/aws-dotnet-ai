// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;

namespace BuildDiagnosticAgent.Services;

/// <summary>
/// GitHub API client adapted from BuildSystemAgent.Services.GitHubClient. Trimmed to
/// the methods BuildDiagnosticAgent needs (issue search and per-file commit history)
/// since PR-diff and file-content reads are owned by BuildSystemAgent.
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
    /// Searches issues and pull requests across GitHub. Pass a fully-formed search query
    /// (e.g. <c>"\"TestName\" repo:aws/aws-dotnet-ai created:>2026-04-01"</c>).
    /// </summary>
    public async Task<string> SearchIssuesAsync(string query, CancellationToken ct = default)
    {
        var encoded = Uri.EscapeDataString(query);
        var response = await GetAsync($"/search/issues?q={encoded}&per_page=10", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Returns the most recent commits that touched <paramref name="path"/>, starting
    /// from <paramref name="sha"/> (a branch name or commit SHA). Up to 3 results.
    /// </summary>
    public async Task<string> GetCommitsForPathAsync(string owner, string repo, string path, string sha, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var encodedSha = Uri.EscapeDataString(sha);
        var response = await GetAsync($"/repos/{owner}/{repo}/commits?path={encodedPath}&sha={encodedSha}&per_page=3", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

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

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("GitHub API returned 401, refreshing token");
            _cachedToken = null;
            token = await GetTokenAsync(ct);

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

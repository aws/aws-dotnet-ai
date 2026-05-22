// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Json;
using Aspire.Hosting.Testing;

namespace AWS.AgentCore.Testing.IntegrationTests;

/// <summary>
/// Integration tests that use Aspire.Hosting.Testing's DistributedApplicationTestingBuilder
/// to spin up the full Aspire stack with embedded emulators and verify end-to-end request flow.
/// Requires AWS credentials for Bedrock LLM calls (AnnotationsSample uses Claude).
/// </summary>
public class AspireIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private ResourceNotificationService _resourceNotificationService = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.AspireAppHost>();

        _app = await builder.BuildAsync();
        _resourceNotificationService = _app.Services.GetRequiredService<ResourceNotificationService>();

        await _app.StartAsync();

        // Wait for the agent to be in a running state
        await _resourceNotificationService.WaitForResourceAsync("AnnotationsSample", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Agent_PingEndpoint_ReturnsHealthy()
    {
        // Arrange
        using var httpClient = _app.CreateHttpClient("AnnotationsSample");
        var ct = TestContext.Current.CancellationToken;

        // Act
        var response = await httpClient.GetAsync("/ping", ct);

        // Assert
        Assert.True(response.IsSuccessStatusCode,
            $"Ping endpoint returned status {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task Agent_InvocationsEndpoint_ReturnsResponse()
    {
        // Arrange
        using var httpClient = _app.CreateHttpClient("AnnotationsSample");
        httpClient.Timeout = TimeSpan.FromMinutes(3); // LLM calls can be slow
        var ct = TestContext.Current.CancellationToken;

        // Act
        var response = await httpClient.PostAsJsonAsync("/invocations", new { prompt = "What is 2+2? Reply with just the number." }, ct);

        // Assert
        Assert.True(response.IsSuccessStatusCode,
            $"Invocations endpoint returned status {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.NotEmpty(body);
        Assert.Contains("message", body); // Response should be JSON with a "message" field
    }

    [Fact]
    public async Task Agent_InvocationsEndpoint_ReturnsJsonWithTimestamp()
    {
        // Arrange
        using var httpClient = _app.CreateHttpClient("AnnotationsSample");
        httpClient.Timeout = TimeSpan.FromMinutes(3);
        var ct = TestContext.Current.CancellationToken;

        // Act
        var response = await httpClient.PostAsJsonAsync("/invocations", new { prompt = "Say hello" }, ct);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("message", body);
        Assert.Contains("timestamp", body);
    }
}

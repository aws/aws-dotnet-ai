// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AWS.AgentCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AWS.AgentCore.UnitTests;

public class AgentCoreEndpointExtensionsTests : IAsyncDisposable
{
    private WebApplication? _app;

    [Fact]
    public async Task PingEndpoint_ReturnsHealthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync();

        var response = await client.GetAsync("/ping", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("healthy", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvocationsEndpoint_ReturnsHandlerResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync();

        var response = await client.PostAsJsonAsync("/invocations", new TestRequest("Hello"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("echo: Hello", json.GetProperty("message").GetString());
        Assert.True(json.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task InvocationsEndpoint_WithNullBody_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync();

        var response = await client.PostAsync("/invocations",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Invalid request body.", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvocationsEndpoint_PassesChatClientFromDI()
    {
        var ct = TestContext.Current.CancellationToken;
        IChatClient? capturedClient = null;

        var (app, client) = await CreateTestAppAsync(handler: async (request, chatClient, _) =>
        {
            capturedClient = chatClient;
            return "ok";
        });

        await client.PostAsJsonAsync("/invocations", new TestRequest("test"), ct);

        Assert.NotNull(capturedClient);
    }

    private async Task<(WebApplication app, HttpClient client)> CreateTestAppAsync(
        Func<TestRequest, IChatClient, CancellationToken, Task<string>>? handler = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        _app = builder.Build();

        handler ??= (request, chatClient, ct) => Task.FromResult($"echo: {request.Input}");

        _app.MapAgentCore<TestRequest>(handler);

        await _app.StartAsync();

        var client = new HttpClient
        {
            BaseAddress = new Uri(_app.Urls.First())
        };

        return (_app, client);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

public record TestRequest(string? Input);

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AWS.AgentCore.Hosting;
using AWS.AgentCore.Hosting.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace AWS.AgentCore.Hosting.UnitTests;

public class AgentCoreEndpointExtensionsTests : IAsyncDisposable
{
    private WebApplication? _app;

    [Fact]
    public async Task PingEndpoint_ReturnsHealthy()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            (TestRequest request) => Task.FromResult($"echo: {request.Input}"));

        var response = await client.GetAsync("/ping", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.True(json.TryGetProperty("time_of_last_update", out var timestamp));
        Assert.True(timestamp.GetInt64() > 0);
    }

    [Fact]
    public async Task InvocationsEndpoint_ReturnsHandlerResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            (TestRequest request) => Task.FromResult($"echo: {request.Input}"));

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
        var (app, client) = await CreateTestAppAsync(
            (TestRequest request) => Task.FromResult("ok"));

        var response = await client.PostAsync("/invocations",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("Invalid request body.", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task InvocationsEndpoint_ResolvesChatClientFromDI()
    {
        var ct = TestContext.Current.CancellationToken;
        IChatClient? capturedClient = null;

        var (app, client) = await CreateTestAppAsync(
            async (TestRequest request, IChatClient chatClient) =>
            {
                capturedClient = chatClient;
                return "ok";
            });

        await client.PostAsJsonAsync("/invocations", new TestRequest("test"), ct);

        Assert.NotNull(capturedClient);
    }

    [Fact]
    public async Task InvocationsEndpoint_ResolvesMultipleServicesFromDI()
    {
        var ct = TestContext.Current.CancellationToken;
        IChatClient? capturedClient = null;
        ILogger<AgentCoreEndpointExtensionsTests>? capturedLogger = null;

        var (app, client) = await CreateTestAppAsync(
            async (TestRequest request, IChatClient chatClient,
                ILogger<AgentCoreEndpointExtensionsTests> logger) =>
            {
                capturedClient = chatClient;
                capturedLogger = logger;
                return "ok";
            });

        await client.PostAsJsonAsync("/invocations", new TestRequest("test"), ct);

        Assert.NotNull(capturedClient);
        Assert.NotNull(capturedLogger);
    }

    [Fact]
    public async Task InvocationsEndpoint_BindsRuntimeContext()
    {
        var ct = TestContext.Current.CancellationToken;
        AgentCoreRuntimeContext? capturedContext = null;

        var (app, client) = await CreateTestAppAsync(
            async (TestRequest request, AgentCoreRuntimeContext context) =>
            {
                capturedContext = context;
                return "ok";
            });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/invocations");
        httpRequest.Content = JsonContent.Create(new TestRequest("test"));
        httpRequest.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id", "session-123");
        httpRequest.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id", "req-456");
        httpRequest.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Custom-MyKey", "my-value");

        await client.SendAsync(httpRequest, ct);

        Assert.NotNull(capturedContext);
        Assert.Equal("session-123", capturedContext.SessionId);
        Assert.Equal("req-456", capturedContext.RequestId);
        Assert.Equal("my-value", capturedContext.CustomHeaders["MyKey"]);
    }

    [Fact]
    public async Task InvocationsEndpoint_BindsCancellationToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var tokenWasBound = false;

        var (app, client) = await CreateTestAppAsync(
            async (TestRequest request, CancellationToken cancellationToken) =>
            {
                tokenWasBound = cancellationToken.CanBeCanceled;
                return "ok";
            });

        await client.PostAsJsonAsync("/invocations", new TestRequest("test"), ct);

        Assert.True(tokenWasBound);
    }

    [Fact]
    public async Task PingEndpoint_WithCustomHandler_ReturnsCustomResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            handler: (TestRequest request) => Task.FromResult("ok"),
            pingHandler: () => new { status = "custom", version = "1.0" });

        var response = await client.GetAsync("/ping", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("custom", json.GetProperty("status").GetString());
        Assert.Equal("1.0", json.GetProperty("version").GetString());
    }

    [Fact]
    public async Task PingEndpoint_WithCustomHandler_ResolvesServicesFromDI()
    {
        var ct = TestContext.Current.CancellationToken;
        IChatClient? capturedClient = null;

        var (app, client) = await CreateTestAppAsync(
            handler: (TestRequest request) => Task.FromResult("ok"),
            pingHandler: (IChatClient chatClient) =>
            {
                capturedClient = chatClient;
                return new { status = "healthy" };
            });

        await client.GetAsync("/ping", ct);

        Assert.NotNull(capturedClient);
    }

    [Fact]
    public async Task PingEndpoint_WithAsyncCustomHandler_ReturnsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            handler: (TestRequest request) => Task.FromResult("ok"),
            pingHandler: async (CancellationToken cancellationToken) =>
            {
                await Task.CompletedTask;
                return (object)new { status = "healthy", async = true };
            });

        var response = await client.GetAsync("/ping", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("healthy", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("async").GetBoolean());
    }

    [Fact]
    public async Task InvocationsEndpoint_StreamingHandler_ReturnsSSE()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            StreamingHandler());

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/invocations");
        httpRequest.Content = JsonContent.Create(new TestRequest("test"));

        var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(ct);
        var lines = body.Split("\n", StringSplitOptions.RemoveEmptyEntries);

        // Should have 3 chunk events + 1 done event
        var dataLines = lines.Where(l => l.StartsWith("data: ")).ToList();
        Assert.Equal(4, dataLines.Count);

        // Verify chunks
        var chunk1 = JsonDocument.Parse(dataLines[0]["data: ".Length..]);
        Assert.Equal("Hello ", chunk1.RootElement.GetProperty("chunk").GetString());

        var chunk2 = JsonDocument.Parse(dataLines[1]["data: ".Length..]);
        Assert.Equal("World", chunk2.RootElement.GetProperty("chunk").GetString());

        var chunk3 = JsonDocument.Parse(dataLines[2]["data: ".Length..]);
        Assert.Equal("!", chunk3.RootElement.GetProperty("chunk").GetString());

        // Verify done event
        var done = JsonDocument.Parse(dataLines[3]["data: ".Length..]);
        Assert.Equal("Hello World!", done.RootElement.GetProperty("message").GetString());
        Assert.True(done.RootElement.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task InvocationsEndpoint_StreamingHandler_ResolvesServicesFromDI()
    {
        var ct = TestContext.Current.CancellationToken;
        IChatClient? capturedClient = null;

        var (app, client) = await CreateTestAppAsync(
            StreamingHandlerWithDI(c => capturedClient = c));

        await client.PostAsJsonAsync("/invocations", new TestRequest("test"), ct);

        Assert.NotNull(capturedClient);
    }

    [Fact]
    public async Task InvocationsEndpoint_StreamingHandler_SkipsEmptyChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var (app, client) = await CreateTestAppAsync(
            StreamingHandlerWithEmptyChunks());

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/invocations");
        httpRequest.Content = JsonContent.Create(new TestRequest("test"));

        var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var dataLines = body.Split("\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("data: ")).ToList();

        // 1 chunk + 1 done (empty chunks skipped)
        Assert.Equal(2, dataLines.Count);

        var chunk = JsonDocument.Parse(dataLines[0]["data: ".Length..]);
        Assert.Equal("content", chunk.RootElement.GetProperty("chunk").GetString());
    }

    // Helper methods that return Delegate for streaming handlers.
    // Using static methods because async iterators can't be lambdas in C#.

    private static Delegate StreamingHandler()
    {
        return (TestRequest request) => StreamChunks();

        static async IAsyncEnumerable<string> StreamChunks()
        {
            yield return "Hello ";
            yield return "World";
            yield return "!";
        }
    }

    private static Delegate StreamingHandlerWithDI(Action<IChatClient> capture)
    {
        return (TestRequest request, IChatClient chatClient) =>
        {
            capture(chatClient);
            return StreamChunks();
        };

        static async IAsyncEnumerable<string> StreamChunks()
        {
            yield return "chunk";
        }
    }

    private static Delegate StreamingHandlerWithEmptyChunks()
    {
        return (TestRequest request) => StreamChunks();

        static async IAsyncEnumerable<string> StreamChunks()
        {
            yield return "";
            yield return "content";
            yield return "";
        }
    }

    private async Task<(WebApplication app, HttpClient client)> CreateTestAppAsync(Delegate handler)
        => await CreateTestAppAsync(handler, pingHandler: null);

    private async Task<(WebApplication app, HttpClient client)> CreateTestAppAsync(
        Delegate handler, Delegate? pingHandler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);
        builder.Services.AddLogging();

        _app = builder.Build();

        _app.MapAgentCore<TestRequest>(handler, pingHandler);

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

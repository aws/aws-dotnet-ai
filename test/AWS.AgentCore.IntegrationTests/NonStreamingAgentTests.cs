// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.IntegrationTests.Infrastructure;

namespace AWS.AgentCore.IntegrationTests;

/// <summary>
/// The prompt used to get deterministic app info from the agent.
/// Forces the LLM to call the GetAppInfo tool and return the raw JSON.
/// </summary>
internal static class AppInfoPrompt
{
    internal const string Prompt = "Call the GetAppInfo tool and respond with ONLY the exact JSON it returns. Do not add any other text, explanation, or formatting. Just the raw JSON object.";
}

public class MicrosoftAgentFrameworkTests : IClassFixture<MicrosoftAgentFrameworkFixture>, IDisposable
{
    private readonly MicrosoftAgentFrameworkFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public MicrosoftAgentFrameworkTests(MicrosoftAgentFrameworkFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Invoke_ReturnsValidJsonResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, "Hello", ct);

        Assert.Equal(200, result.HttpStatusCode);
        var doc = System.Text.Json.JsonDocument.Parse(result.RawBody);
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
        Assert.True(doc.RootElement.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Invoke_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("MicrosoftAgentFrameworkSample", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsNotNativeAot()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        // Non-AOT apps report isNativeAot: false
        Assert.Contains("\"isNativeAot\":false", result.Message.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_WeatherToolExecutesThroughMiddleware()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What is the weather in Seattle? Respond with only the weather information, nothing else.",
            ct);

        Assert.Equal(200, result.HttpStatusCode);
        // The weather tool should have been called through the function-calling middleware
        // and returned a result containing the hardcoded temperature
        Assert.Contains("72", result.Message);
    }

    [Fact]
    public async Task Invoke_FlightSearchToolExecutesThroughMiddleware()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "Search for flights from NYC to LA on 2026-06-15. Respond with only the flight information.",
            ct);

        Assert.Equal(200, result.HttpStatusCode);
        // The flight search tool should have been called through the function-calling middleware
        Assert.Contains("NYC", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LA", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_MiddlewareLogsVisibleInCloudWatch()
    {
        var ct = TestContext.Current.CancellationToken;

        // Invoke with a prompt that triggers a tool call
        await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What is the weather in Tokyo?",
            ct);

        // Poll CloudWatch until middleware markers appear
        var logs = await CloudWatchLogHelper.WaitForLogsContainingAsync(
            _fixture.RuntimeArn,
            _fixture.Region,
            ["[Middleware] Agent run starting", "[Middleware] Agent run complete", "[ToolMiddleware] Calling tool:"],
            timeout: TimeSpan.FromSeconds(90),
            ct: ct);

        // Agent middleware should have logged
        Assert.Contains("[Middleware] Agent run starting", logs);
        Assert.Contains("[Middleware] Agent run complete", logs);

        // Function-calling middleware should have logged the tool name
        Assert.Contains("[ToolMiddleware] Calling tool:", logs);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsArm64()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("Arm64", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

public class AnnotationsSampleTests : IClassFixture<AnnotationsSampleFixture>, IDisposable
{
    private readonly AnnotationsSampleFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public AnnotationsSampleTests(AnnotationsSampleFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("AnnotationsSample", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsNotNativeAot()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("\"isNativeAot\":false", result.Message.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_S3BucketCountReturnsNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "Call the GetS3BucketCount tool and respond with ONLY the exact JSON it returns. Do not add any other text.",
            ct);

        Assert.Equal(200, result.HttpStatusCode);
        // The tool must successfully call S3 — meaning credentials resolved correctly.
        // If credentials fail, the tool returns {"error":"...", "message":"..."} instead.
        Assert.Contains("bucketCount", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

public class NativeAotExtensionsTests : IClassFixture<NativeAotExtensionsFixture>, IDisposable
{
    private readonly NativeAotExtensionsFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public NativeAotExtensionsTests(NativeAotExtensionsFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Invoke_ReturnsNonEmptyResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, "Say hello in exactly 3 words.", ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task Invoke_ReturnsValidJsonResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, "Hello", ct);

        Assert.Equal(200, result.HttpStatusCode);
        var doc = System.Text.Json.JsonDocument.Parse(result.RawBody);
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Invoke_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("NativeAotExtensions", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsNativeAot()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        // NativeAOT apps report isNativeAot: true
        Assert.Contains("\"isNativeAot\":true", result.Message.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsArm64()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("Arm64", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

public class NativeAotAnnotationsTests : IClassFixture<NativeAotAnnotationsFixture>, IDisposable
{
    private readonly NativeAotAnnotationsFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public NativeAotAnnotationsTests(NativeAotAnnotationsFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Invoke_ReturnsNonEmptyResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, "Say hello in exactly 3 words.", ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task Invoke_ReturnsValidJsonResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, "Hello", ct);

        Assert.Equal(200, result.HttpStatusCode);
        var doc = System.Text.Json.JsonDocument.Parse(result.RawBody);
        Assert.True(doc.RootElement.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Invoke_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("NativeAotAnnotations", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsNativeAot()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        // NativeAOT apps report isNativeAot: true
        Assert.Contains("\"isNativeAot\":true", result.Message.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invoke_AppInfoReportsArm64()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("Arm64", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

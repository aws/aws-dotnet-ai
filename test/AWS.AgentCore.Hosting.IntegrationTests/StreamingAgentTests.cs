// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting.IntegrationTests.Infrastructure;

namespace AWS.AgentCore.Hosting.IntegrationTests;

public class StreamingAgentTests : IClassFixture<StreamingAgentFixture>, IDisposable
{
    private readonly StreamingAgentFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public StreamingAgentTests(StreamingAgentFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task InvokeStreaming_ReturnsMultipleChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeStreamingAsync(_fixture.RuntimeArn, "What is the weather in Seattle?", ct: ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.True(result.Chunks.Count > 1, $"Expected multiple chunks, got {result.Chunks.Count}");
    }

    [Fact]
    public async Task InvokeStreaming_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeStreamingAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct: ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("StreamingAgent", result.FinalMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeStreaming_AppInfoReportsNotNativeAot()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeStreamingAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct: ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("\"isNativeAot\":false", result.FinalMessage.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

public class AnnotationsStreamingAgentTests : IClassFixture<AnnotationsStreamingAgentFixture>, IDisposable
{
    private readonly AnnotationsStreamingAgentFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public AnnotationsStreamingAgentTests(AnnotationsStreamingAgentFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task InvokeStreaming_ReturnsMultipleChunks()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeStreamingAsync(_fixture.RuntimeArn, "What is the weather in Seattle?", ct: ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.True(result.Chunks.Count > 1, $"Expected multiple chunks, got {result.Chunks.Count}");
    }

    [Fact]
    public async Task InvokeStreaming_AppInfoReportsCorrectName()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await _invoker.InvokeStreamingAsync(_fixture.RuntimeArn, AppInfoPrompt.Prompt, ct: ct);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains("AnnotationsStreamingAgent", result.FinalMessage, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _invoker.Dispose();
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.IntegrationTests.Infrastructure;

namespace AWS.AgentCore.IntegrationTests;

/// <summary>
/// Memory integration tests for non-streaming sample apps.
/// Each test tells the agent a unique piece of information, then asks about it
/// in a separate invocation using the same session ID to verify memory persistence.
/// </summary>
public class MicrosoftAgentFrameworkMemoryTests : IClassFixture<MicrosoftAgentFrameworkFixture>, IDisposable
{
    private readonly MicrosoftAgentFrameworkFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public MicrosoftAgentFrameworkMemoryTests(MicrosoftAgentFrameworkFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-msaf-{Guid.NewGuid():N}";
        var secretCode = $"ALPHA-{Random.Shared.Next(1000, 9999)}";

        // First invocation: tell the agent a unique piece of information
        await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        // Brief delay to allow memory persistence
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        // Second invocation: ask the agent to recall the information
        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.Message);
    }

    public void Dispose() => _invoker.Dispose();
}

public class AnnotationsSampleMemoryTests : IClassFixture<AnnotationsSampleFixture>, IDisposable
{
    private readonly AnnotationsSampleFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public AnnotationsSampleMemoryTests(AnnotationsSampleFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-annotations-{Guid.NewGuid():N}";
        var secretCode = $"BRAVO-{Random.Shared.Next(1000, 9999)}";

        await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.Message);
    }

    public void Dispose() => _invoker.Dispose();
}

public class NativeAotExtensionsMemoryTests : IClassFixture<NativeAotExtensionsFixture>, IDisposable
{
    private readonly NativeAotExtensionsFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public NativeAotExtensionsMemoryTests(NativeAotExtensionsFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-aot-ext-{Guid.NewGuid():N}";
        var secretCode = $"CHARLIE-{Random.Shared.Next(1000, 9999)}";

        await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.Message);
    }

    public void Dispose() => _invoker.Dispose();
}

public class NativeAotAnnotationsMemoryTests : IClassFixture<NativeAotAnnotationsFixture>, IDisposable
{
    private readonly NativeAotAnnotationsFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public NativeAotAnnotationsMemoryTests(NativeAotAnnotationsFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-aot-ann-{Guid.NewGuid():N}";
        var secretCode = $"DELTA-{Random.Shared.Next(1000, 9999)}";

        await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var result = await _invoker.InvokeAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.Message);
    }

    public void Dispose() => _invoker.Dispose();
}

public class StreamingAgentMemoryTests : IClassFixture<StreamingAgentFixture>, IDisposable
{
    private readonly StreamingAgentFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public StreamingAgentMemoryTests(StreamingAgentFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-streaming-{Guid.NewGuid():N}";
        var secretCode = $"ECHO-{Random.Shared.Next(1000, 9999)}";

        // Use streaming invocation for both calls
        await _invoker.InvokeStreamingAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var result = await _invoker.InvokeStreamingAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.FinalMessage);
    }

    public void Dispose() => _invoker.Dispose();
}

public class AnnotationsStreamingAgentMemoryTests : IClassFixture<AnnotationsStreamingAgentFixture>, IDisposable
{
    private readonly AnnotationsStreamingAgentFixture _fixture;
    private readonly AgentCoreInvoker _invoker;

    public AnnotationsStreamingAgentMemoryTests(AnnotationsStreamingAgentFixture fixture)
    {
        _fixture = fixture;
        _invoker = new AgentCoreInvoker(_fixture.Region);
    }

    [Fact]
    public async Task Memory_RemembersInformationAcrossInvocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"memory-test-ann-stream-{Guid.NewGuid():N}";
        var secretCode = $"FOXTROT-{Random.Shared.Next(1000, 9999)}";

        await _invoker.InvokeStreamingAsync(
            _fixture.RuntimeArn,
            $"Remember this secret code: {secretCode}. Just confirm you've noted it.",
            ct,
            sessionId: sessionId);

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var result = await _invoker.InvokeStreamingAsync(
            _fixture.RuntimeArn,
            "What was the secret code I told you earlier? Reply with ONLY the code, nothing else.",
            ct,
            sessionId: sessionId);

        Assert.Equal(200, result.HttpStatusCode);
        Assert.Contains(secretCode, result.FinalMessage);
    }

    public void Dispose() => _invoker.Dispose();
}

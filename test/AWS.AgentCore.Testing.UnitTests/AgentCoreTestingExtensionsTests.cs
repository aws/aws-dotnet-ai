// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Testing.UnitTests;

/// <summary>
/// A fake project metadata type for testing AddProject calls.
/// Points to a real csproj so Aspire's launch profile validation doesn't fail.
/// </summary>
internal class FakeAgentProject : IProjectMetadata
{
    public string ProjectPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
            "AWS.AgentCore.Testing", "AWS.AgentCore.Testing.csproj"));
}

/// <summary>
/// Unit tests for AgentCoreTestingExtensions verifying the public API surface:
/// AddAgentCoreRuntime, WithInMemory, WithStreaming, and WithReference.
/// </summary>
public class AgentCoreTestingExtensionsTests
{
    // ──────────────────────────────────────────────────────────────────
    // AddAgentCoreRuntime Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AddAgentCoreRuntime_RegistersAgentProjectResource()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>();

        Assert.NotNull(result);
        Assert.Equal("FakeAgentProject", result.Resource.Name);
    }

    [Fact]
    public void AddAgentCoreRuntime_WithCustomName_UsesProvidedName()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>("my-agent");

        Assert.Equal("my-agent", result.Resource.Name);
    }

    [Fact]
    public void AddAgentCoreRuntime_SetsAspireManagedEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>();

        var envAnnotations = result.Resource.Annotations
            .OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envAnnotations);
    }

    [Fact]
    public void AddAgentCoreRuntime_AddsHttpEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>();

        var endpoints = result.Resource.Annotations
            .OfType<EndpointAnnotation>();
        Assert.NotEmpty(endpoints);
        Assert.Contains(endpoints, e => e.Name == "http");
    }

    [Fact]
    public void AddAgentCoreRuntime_RegistersAnnotationForDeferredUrl()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>();

        // Chat URL is added during BeforeResourceStartedEvent (after emulators start)
        // At registration time, the annotation exists but no URL yet
        var annotation = result.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .FirstOrDefault();
        Assert.NotNull(annotation);
    }

    [Fact]
    public void AddAgentCoreRuntime_RegistersAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>();

        var annotation = result.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .FirstOrDefault();
        Assert.NotNull(annotation);
        // Ports are 0 before emulators start (deferred resolution)
        Assert.Equal(0, annotation.RuntimePort);
        Assert.Equal(0, annotation.ChatAppPort);
    }

    [Fact]
    public void AddAgentCoreRuntime_ThrowsOnNullBuilder()
    {
        IDistributedApplicationBuilder builder = null!;

        Assert.Throws<ArgumentNullException>(() =>
            builder.AddAgentCoreRuntime<FakeAgentProject>());
    }

    // ──────────────────────────────────────────────────────────────────
    // WithInMemory Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithInMemory_SetsMemoryIdEnvironmentVariable()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>()
            .WithInMemory();

        // AWS_AGENTCORE_MEMORY_ID is set as a plain string (not deferred)
        // Verify via the synchronous env callbacks only
        var envVars = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(
                new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)),
            result.Resource,
            envVars);

        foreach (var annotation in result.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            var task = annotation.Callback(context);
            if (task.IsCompleted)
                await task;
        }

        Assert.True(envVars.ContainsKey("AWS_AGENTCORE_MEMORY_ID"));
        Assert.Equal("localdev-memory", envVars["AWS_AGENTCORE_MEMORY_ID"]?.ToString());
    }

    [Fact]
    public void WithInMemory_RegistersServiceEndpointCallback()
    {
        var builder = DistributedApplication.CreateBuilder();

        var result = builder.AddAgentCoreRuntime<FakeAgentProject>()
            .WithInMemory();

        // The service endpoint is set via a deferred async callback
        var envCallbacks = result.Resource.Annotations
            .OfType<EnvironmentCallbackAnnotation>();
        Assert.NotEmpty(envCallbacks);

        // The annotation should be marked for memory
        var annotation = result.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .First();
        Assert.True(annotation.HasMemory);
    }

    [Fact]
    public void WithInMemory_ReturnsSameBuilderForChaining()
    {
        var builder = DistributedApplication.CreateBuilder();
        var runtimeBuilder = builder.AddAgentCoreRuntime<FakeAgentProject>();

        var result = runtimeBuilder.WithInMemory();

        Assert.Same(runtimeBuilder, result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Multiple Agents Tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleAgents_EachGetOwnAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder();

        var agent1 = builder.AddAgentCoreRuntime<FakeAgentProject>("agent-1");
        var agent2 = builder.AddAgentCoreRuntime<FakeAgentProject>("agent-2");

        var annotation1 = agent1.Resource.Annotations.OfType<AgentCoreRuntimeAnnotation>().First();
        var annotation2 = agent2.Resource.Annotations.OfType<AgentCoreRuntimeAnnotation>().First();

        Assert.NotSame(annotation1, annotation2);
    }

    [Fact]
    public void MultipleAgents_GetUniqueResourceNames()
    {
        var builder = DistributedApplication.CreateBuilder();

        var agent1 = builder.AddAgentCoreRuntime<FakeAgentProject>("agent-1");
        var agent2 = builder.AddAgentCoreRuntime<FakeAgentProject>("agent-2");

        Assert.Equal("agent-1", agent1.Resource.Name);
        Assert.Equal("agent-2", agent2.Resource.Name);
    }

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, object>> GetEnvironmentVariablesAsync(IResource resource)
    {
        var envVars = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(
                new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)),
            resource,
            envVars);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return envVars;
    }
}

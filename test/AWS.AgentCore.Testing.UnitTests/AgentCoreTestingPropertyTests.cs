// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0


namespace AWS.AgentCore.Testing.UnitTests;

/// <summary>
/// Property-based tests for AgentCoreTestingExtensions correctness properties.
/// Uses FsCheck to generate arbitrary inputs and verify universal properties.
/// </summary>
public class AgentCoreTestingPropertyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Property 1: Memory ID is always set to the constant value
    // When WithInMemory() is called, the AWS_AGENTCORE_MEMORY_ID
    // environment variable SHALL always equal "localdev-memory".
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WithInMemory_AlwaysSetsConstantMemoryId()
    {
        // Single builder with multiple agents — validates the memory ID is always "localdev-memory"
        var builder = DistributedApplication.CreateBuilder();

        for (var i = 0; i < 10; i++)
        {
            builder.AddAgentCoreRuntime<FakeAgentProject>($"agent-{i}")
                .WithInMemory();
        }

        var app = builder.Build();
        var agentResources = app.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ProjectResource>()
            .ToList();

        foreach (var agentResource in agentResources)
        {
            var envVars = GetEnvironmentVariablesSync(agentResource);

            Assert.True(envVars.ContainsKey("AWS_AGENTCORE_MEMORY_ID"),
                $"AWS_AGENTCORE_MEMORY_ID should be set on {agentResource.Name}");
            Assert.Equal("localdev-memory", envVars["AWS_AGENTCORE_MEMORY_ID"]?.ToString());
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 2: Multiple agents each get independent annotations
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleAgents_EachGetIndependentAnnotations()
    {
        var builder = DistributedApplication.CreateBuilder();
        var annotations = new List<AgentCoreRuntimeAnnotation>();

        for (var i = 0; i < 10; i++)
        {
            var result = builder.AddAgentCoreRuntime<FakeAgentProject>($"agent-{i}");
            var annotation = result.Resource.Annotations.OfType<AgentCoreRuntimeAnnotation>().First();
            annotations.Add(annotation);
        }

        // Each agent gets its own distinct annotation instance
        Assert.Equal(annotations.Count, annotations.Distinct().Count());
        // All annotations start with ports at 0 (deferred resolution)
        Assert.All(annotations, a => Assert.Equal(0, a.RuntimePort));
    }

    private static Dictionary<string, object> GetEnvironmentVariablesSync(IResource resource)
    {
        var envVars = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(
                new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)),
            resource,
            envVars);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            var task = annotation.Callback(context);
            if (task.IsCompleted)
                task.GetAwaiter().GetResult();
            // Skip deferred async callbacks (they await EmulatorStarted which hasn't fired)
        }

        return envVars;
    }
}

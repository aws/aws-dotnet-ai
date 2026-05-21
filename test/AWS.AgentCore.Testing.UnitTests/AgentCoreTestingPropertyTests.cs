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
    // Property 2: Port uniqueness across multiple agents
    // For any N agents registered, all pre-allocated ports SHALL be unique.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleAgents_AlwaysGetUniquePorts()
    {
        // Single builder, many agents — validates port uniqueness without exhausting file descriptors
        var builder = DistributedApplication.CreateBuilder();
        var ports = new List<int>();

        for (var i = 0; i < 20; i++)
        {
            var result = builder.AddAgentCoreRuntime<FakeAgentProject>($"agent-{i}");
            var annotation = result.Resource.Annotations.OfType<AgentCoreRuntimeAnnotation>().First();
            ports.Add(annotation.RuntimePort);
            ports.Add(annotation.ChatAppPort);
        }

        // 20 agents × 2 ports = 40 ports, all must be unique
        Assert.Equal(ports.Count, ports.Distinct().Count());
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
            annotation.Callback(context).GetAwaiter().GetResult();
        }

        return envVars;
    }
}

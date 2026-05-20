// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using PortAllocator = AWS.AgentCore.Testing.Services.PortAllocator;

namespace AWS.AgentCore.Testing;

/// <summary>
/// Aspire hosting extension methods for adding AgentCore local testing components.
/// All emulators run as embedded in-process Kestrel servers — no Docker or separate
/// processes required.
/// </summary>
public static class AgentCoreTestingExtensions
{
    /// <summary>
    /// Registers an AgentCore agent with a dedicated runtime emulator and chat app.
    /// Returns the agent's <see cref="IResourceBuilder{ProjectResource}"/> which is
    /// compatible with Aspire's deployment features (e.g., PublishAsECSFargateService).
    /// Use <c>.WithReference(agent)</c> from another project to inject the runtime
    /// emulator endpoint as the <c>AGENTCORE_SERVICE_ENDPOINT</c> environment variable.
    /// </summary>
    public static IResourceBuilder<ProjectResource> AddAgentCoreRuntime<TProject>(
        this IDistributedApplicationBuilder builder,
        string? name = null)
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var projectName = name ?? typeof(TProject).Name
            .Replace("_", "-");

        var runtimePort = PortAllocator.GetAvailablePort();
        var chatAppPort = PortAllocator.GetAvailablePort();
        var runtimeUrl = $"http://localhost:{runtimePort}";
        var chatAppUrl = $"http://localhost:{chatAppPort}";

        var agentApp = builder.AddProject<TProject>(projectName)
            .WithHttpEndpoint(name: "http")
            .WithEnvironment("AWS_AGENTCORE_ASPIRE_MANAGED", "true");

        agentApp.WithUrlForEndpoint("http", url => url.DisplayText = "Agent");
        agentApp.WithUrl(chatAppUrl, "Chat");

        // Store runtime metadata on the resource
        agentApp.Resource.Annotations.Add(new AgentCoreRuntimeAnnotation(runtimePort, chatAppPort));

        // Start emulators after all resources are created
        builder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (@event, ct) =>
        {
            var notificationService = @event.Services.GetRequiredService<ResourceNotificationService>();
            var loggerService = @event.Services.GetRequiredService<ResourceLoggerService>();

            var agentEndpointUrl = "http://localhost:8080";
            var endpointAnnotation = agentApp.Resource.Annotations
                .OfType<EndpointAnnotation>()
                .FirstOrDefault(e => e.Name == "http");

            if (endpointAnnotation?.AllocatedEndpoint != null)
            {
                agentEndpointUrl = endpointAnnotation.AllocatedEndpoint.UriString;
            }

            var annotation = agentApp.Resource.Annotations
                .OfType<AgentCoreRuntimeAnnotation>()
                .First();

            // Start runtime emulator
            var runtimeLoggerProvider = new Services.AspireLoggerProvider(
                loggerService.GetLogger(agentApp.Resource));

            var runtimeApp = RuntimeEmulatorServer.Create(agentEndpointUrl, port: annotation.RuntimePort, loggerProvider: runtimeLoggerProvider);
            await runtimeApp.StartAsync(ct);

            // Start chat app
            var chatApp = ChatAppServer.Create(runtimeUrl, port: annotation.ChatAppPort, streaming: annotation.IsStreaming);
            await chatApp.StartAsync(ct);
        });

        return agentApp;
    }

    /// <summary>
    /// Wires a project to an AgentCore agent by injecting the runtime emulator endpoint
    /// as the <c>AGENTCORE_SERVICE_ENDPOINT</c> environment variable.
    /// The consuming project can use this as the AWS SDK's ServiceURL override.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithReference(
        this IResourceBuilder<ProjectResource> project,
        IResourceBuilder<ProjectResource> agent)
    {
        var annotation = agent.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .FirstOrDefault();

        if (annotation is null)
        {
            throw new InvalidOperationException(
                "The referenced resource is not an AgentCore runtime. " +
                "Use AddAgentCoreRuntime<T>() to create it.");
        }

        var runtimeUrl = $"http://localhost:{annotation.RuntimePort}";
        project.WithEnvironment("AGENTCORE_SERVICE_ENDPOINT", runtimeUrl);

        return project;
    }

    /// <summary>
    /// Configures the chat app to use streaming (SSE) mode for this agent.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithStreaming(
        this IResourceBuilder<ProjectResource> agentApp)
    {
        var annotation = agentApp.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .FirstOrDefault();

        if (annotation is not null)
            annotation.IsStreaming = true;

        return agentApp;
    }

    /// <summary>
    /// Adds an embedded memory emulator and wires it to the agent application.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithInMemory(
        this IResourceBuilder<ProjectResource> agentApp)
    {
        var annotation = agentApp.Resource.Annotations
            .OfType<AgentCoreRuntimeAnnotation>()
            .FirstOrDefault();

        if (annotation is null) return agentApp;

        const string memoryId = "localdev-memory";
        var memoryPort = PortAllocator.GetAvailablePort();
        var endpoint = $"http://localhost:{memoryPort}";

        agentApp
            .WithEnvironment("AWS_AGENTCORE_MEMORY_ID", memoryId)
            .WithEnvironment("AWS_AGENTCORE_SERVICE_ENDPOINT", endpoint);

        var appBuilder = agentApp.ApplicationBuilder;
        appBuilder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (@event, ct) =>
        {
            var memoryApp = MemoryEmulatorServer.Create(port: memoryPort);
            await memoryApp.StartAsync(ct);
        });

        return agentApp;
    }
}

/// <summary>
/// Internal annotation attached to an agent's <see cref="ProjectResource"/> by
/// <see cref="AgentCoreTestingExtensions.AddAgentCoreRuntime{TProject}"/>.
/// Stores the pre-allocated ports for the embedded Runtime Emulator and Chat App,
/// and the streaming mode flag. Used by <c>WithReference</c>, <c>WithStreaming</c>,
/// and <c>WithInMemory</c> to locate runtime metadata on the resource without
/// requiring a custom resource type.
/// </summary>
internal class AgentCoreRuntimeAnnotation(int runtimePort, int chatAppPort) : IResourceAnnotation
{
    /// <summary>
    /// The pre-allocated TCP port the Runtime Emulator listens on.
    /// This is the endpoint injected into consuming projects via <c>WithReference</c>.
    /// </summary>
    public int RuntimePort { get; } = runtimePort;

    /// <summary>
    /// The pre-allocated TCP port the embedded Chat App listens on.
    /// </summary>
    public int ChatAppPort { get; } = chatAppPort;

    /// <summary>
    /// Whether the Chat App should use SSE streaming mode for agent responses.
    /// Set by <see cref="AgentCoreTestingExtensions.WithStreaming"/>.
    /// </summary>
    public bool IsStreaming { get; set; }
}

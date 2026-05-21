// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

// TODO: Move these Aspire hosting extensions to the Aspire.Hosting.AWS package.
// The emulator servers (RuntimeEmulatorServer, ChatAppServer, MemoryEmulatorServer)
// will remain in AWS.AgentCore.Testing. This file should become part of the
// Aspire.Hosting.AWS package and call into AWS.AgentCore.Testing to create/start emulators.

using Aspire.Hosting.ApplicationModel;

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

        var agentApp = builder.AddProject<TProject>(projectName)
            .WithHttpEndpoint(name: "http")
            .WithEnvironment("AWS_AGENTCORE_ASPIRE_MANAGED", "true");

        // Customize the endpoint URL display and hide the raw localhost URL from summary
        agentApp.WithUrlForEndpoint("http", url =>
        {
            url.DisplayText = "Agent";
        });

        // Suppress the default https endpoint URL that launch profiles may add
        agentApp.WithUrls(context =>
        {
            context.Urls.RemoveAll(u =>
                u.Endpoint is not null &&
                u.DisplayText != "Agent" &&
                u.DisplayText != "Chat");
        });

        // Store mutable annotation — ports are filled after emulators start
        var annotation = new AgentCoreRuntimeAnnotation();
        agentApp.Resource.Annotations.Add(annotation);


        // Start emulators before the agent resource starts.
        // This guarantees ports are known before env var callbacks fire.
        builder.Eventing.Subscribe<BeforeResourceStartedEvent>(
            agentApp.Resource,
            async (@event, ct) =>
            {
                var loggerService = @event.Services.GetRequiredService<ResourceLoggerService>();

                var agentEndpointUrl = "http://localhost:8080";
                var endpointAnnotation = agentApp.Resource.Annotations
                    .OfType<EndpointAnnotation>()
                    .FirstOrDefault(e => e.Name == "http");

                if (endpointAnnotation?.AllocatedEndpoint != null)
                {
                    agentEndpointUrl = endpointAnnotation.AllocatedEndpoint.UriString;
                }

                // All emulator logs go to the agent's resource log window in the Aspire Dashboard
                var loggerProvider = new Services.AspireLoggerProvider(
                    loggerService.GetLogger(agentApp.Resource));

                // Start runtime emulator on port 0 (OS-assigned)
                var runtimeApp = RuntimeEmulatorServer.Create(agentEndpointUrl, port: 0, loggerProvider: loggerProvider);
                await runtimeApp.StartAsync(ct);
                annotation.RuntimePort = GetBoundPort(runtimeApp);

                var runtimeUrl = $"http://localhost:{annotation.RuntimePort}";

                // Start chat app on port 0 (OS-assigned)
                var chatApp = ChatAppServer.Create(runtimeUrl, port: 0, streaming: annotation.IsStreaming, loggerProvider: loggerProvider);
                await chatApp.StartAsync(ct);
                annotation.ChatAppPort = GetBoundPort(chatApp);

                // Start memory emulator if configured
                if (annotation.HasMemory)
                {
                    var memoryApp = MemoryEmulatorServer.Create(port: 0, loggerProvider: loggerProvider);
                    await memoryApp.StartAsync(ct);
                    annotation.MemoryPort = GetBoundPort(memoryApp);
                }

                // Add Chat URL now that port is known
                agentApp.Resource.Annotations.Add(new ResourceUrlAnnotation
                {
                    Url = $"http://localhost:{annotation.ChatAppPort}",
                    DisplayText = "Chat"
                });

                // Signal that ports are resolved
                annotation.EmulatorStarted.TrySetResult();
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

        // Deferred: waits for emulators to start before resolving the port
        project.WithEnvironment(async context =>
        {
            await annotation.EmulatorStarted.Task;
            context.EnvironmentVariables["AGENTCORE_SERVICE_ENDPOINT"] =
                $"http://localhost:{annotation.RuntimePort}";
        });

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

        annotation.HasMemory = true;

        agentApp.WithEnvironment("AWS_AGENTCORE_MEMORY_ID", "localdev-memory");

        // Deferred: waits for emulators to start before resolving the memory endpoint
        agentApp.WithEnvironment(async context =>
        {
            await annotation.EmulatorStarted.Task;
            context.EnvironmentVariables["AWS_AGENTCORE_SERVICE_ENDPOINT"] =
                $"http://localhost:{annotation.MemoryPort}";
        });

        return agentApp;
    }

    private static int GetBoundPort(WebApplication app)
    {
        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("Emulator server did not bind to any address after StartAsync.");
        return new Uri(address).Port;
    }
}

/// <summary>
/// Internal annotation attached to an agent's <see cref="ProjectResource"/> by
/// <see cref="AgentCoreTestingExtensions.AddAgentCoreRuntime{TProject}"/>.
/// Stores the actual bound ports for the embedded emulators (resolved after startup).
/// Used by <c>WithReference</c>, <c>WithStreaming</c>, and <c>WithInMemory</c>.
/// </summary>
internal class AgentCoreRuntimeAnnotation : IResourceAnnotation
{
    /// <summary>
    /// The actual TCP port the Runtime Emulator bound to (0 until started).
    /// </summary>
    public int RuntimePort { get; set; }

    /// <summary>
    /// The actual TCP port the embedded Chat App bound to (0 until started).
    /// </summary>
    public int ChatAppPort { get; set; }

    /// <summary>
    /// The actual TCP port the Memory Emulator bound to (0 until started).
    /// </summary>
    public int MemoryPort { get; set; }

    /// <summary>
    /// Whether the Chat App should use SSE streaming mode.
    /// Set by <see cref="AgentCoreTestingExtensions.WithStreaming"/>.
    /// </summary>
    public bool IsStreaming { get; set; }

    /// <summary>
    /// Whether a memory emulator should be started.
    /// Set by <see cref="AgentCoreTestingExtensions.WithInMemory"/>.
    /// </summary>
    public bool HasMemory { get; set; }

    /// <summary>
    /// Signals that all emulators are started and ports are known.
    /// Awaited by deferred environment variable callbacks.
    /// </summary>
    public TaskCompletionSource EmulatorStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

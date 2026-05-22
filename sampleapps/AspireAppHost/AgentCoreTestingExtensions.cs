// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Testing;
using AWS.AgentCore.Testing.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AspireAppHost;

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
        string? name = null,
        Action<AgentCoreTestingOptions>? configure = null)
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new AgentCoreTestingOptions();
        configure?.Invoke(options);

        var projectName = name ?? typeof(TProject).Name
            .Replace("_", "-");

        var agentApp = builder.AddProject<TProject>(projectName)
            .WithHttpEndpoint(name: "http")
            .WithEnvironment("AWS_AGENTCORE_ASPIRE_MANAGED", "true");

        // Suppress all default endpoint URLs — we add our own in the desired order
        agentApp.WithUrls(context =>
        {
            context.Urls.RemoveAll(u =>
                u.Endpoint is not null &&
                u.DisplayText != "Chat" &&
                u.DisplayText != "Runtime Emulator" &&
                u.DisplayText != "Agent Instance");
        });

        // Store mutable annotation — ports are filled after emulators start
        var annotation = new AgentCoreRuntimeAnnotation
        {
            IncludeEmulatorLogs = options.IncludeEmulatorLogs
        };
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

                ILoggerProvider? loggerProvider = null;
                if (annotation.IncludeEmulatorLogs)
                {
                    loggerProvider = new AspireLoggerProvider(
                        loggerService.GetLogger(agentApp.Resource));
                }

                // Start runtime emulator on port 0 (OS-assigned)
                var runtimeApp = RuntimeEmulatorServer.Create(agentEndpointUrl, port: 0, loggerProvider: loggerProvider);
                await runtimeApp.StartAsync(ct);
                annotation.RuntimePort = GetBoundPort(runtimeApp);

                var runtimeUrl = $"http://localhost:{annotation.RuntimePort}";

                // Start chat app on port 0 (OS-assigned)
                var chatApp = ChatAppServer.Create(runtimeUrl, port: 0, streaming: annotation.IsStreaming, agentName: projectName, loggerProvider: loggerProvider);
                await chatApp.StartAsync(ct);
                annotation.ChatAppPort = GetBoundPort(chatApp);

                // Start memory emulator if configured
                if (annotation.HasMemory)
                {
                    var memoryApp = MemoryEmulatorServer.Create(port: 0, loggerProvider: loggerProvider);
                    await memoryApp.StartAsync(ct);
                    annotation.MemoryPort = GetBoundPort(memoryApp);
                }

                // Add URLs now that ports are known (order: Chat, Runtime Emulator, Agent Instance)
                agentApp.Resource.Annotations.Add(new ResourceUrlAnnotation
                {
                    Url = $"http://localhost:{annotation.ChatAppPort}",
                    DisplayText = "Chat"
                });
                agentApp.Resource.Annotations.Add(new ResourceUrlAnnotation
                {
                    Url = $"http://localhost:{annotation.RuntimePort}",
                    DisplayText = "Runtime Emulator"
                });
                agentApp.Resource.Annotations.Add(new ResourceUrlAnnotation
                {
                    Url = agentEndpointUrl,
                    DisplayText = "Agent Instance"
                });

                // Signal that ports are resolved
                annotation.EmulatorStarted.TrySetResult();
            });

        return agentApp;
    }

    /// <summary>
    /// Wires a project to an AgentCore agent by injecting the runtime emulator endpoint
    /// as the <c>AWS_ENDPOINT_URL_BEDROCK_AGENTCORE</c> environment variable.
    /// This is the standard AWS SDK service-specific endpoint override — the SDK picks it up
    /// automatically without additional configuration in the consuming project.
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

        // Only one agent reference per project is supported — the SDK endpoint override
        // (AWS_ENDPOINT_URL_BEDROCK_AGENTCORE) is a single value and cannot point to multiple runtimes.
        var hasExistingReference = project.Resource.Annotations
            .OfType<AgentCoreReferenceAnnotation>()
            .Any();

        if (hasExistingReference)
        {
            throw new InvalidOperationException(
                $"Project '{project.Resource.Name}' already has a WithReference to an AgentCore agent. " +
                "Only one AgentCore runtime reference is supported per project because the " +
                "AWS_ENDPOINT_URL_BEDROCK_AGENTCORE environment variable can only point to a single endpoint.");
        }

        project.Resource.Annotations.Add(new AgentCoreReferenceAnnotation());

        // Deferred: waits for emulators to start before resolving the port
        project.WithEnvironment(async context =>
        {
            await annotation.EmulatorStarted.Task;
            context.EnvironmentVariables["AWS_ENDPOINT_URL_BEDROCK_AGENTCORE"] =
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
    public int RuntimePort { get; set; }
    public int ChatAppPort { get; set; }
    public int MemoryPort { get; set; }
    public bool IsStreaming { get; set; }
    public bool HasMemory { get; set; }
    public bool IncludeEmulatorLogs { get; set; }
    public TaskCompletionSource EmulatorStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal class AgentCoreReferenceAnnotation : IResourceAnnotation;

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockAgentCore;
using Amazon.BedrockRuntime;
using AWS.Bedrock.MEAI;
using AWS.AgentCore.Hosting;
using AWS.AgentCore.Hosting.Internal;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for configuring AgentCore services on <see cref="WebApplicationBuilder"/>.
/// </summary>
public static class AgentCoreBuilderExtensions
{
    /// <summary>
    /// Adds the services required by the AgentCore Runtime to the application and configures
    /// the listening port to match the AgentCore Runtime service contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method performs the following registrations:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///     Registers <see cref="AgentCoreOptions"/> as a singleton, applying any overrides from
    ///     the optional <paramref name="configure"/> callback.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Configures the application to listen on <c>http://0.0.0.0:{port}</c>. The AgentCore Runtime
    ///     expects the application to listen on port <b>8080</b> by default.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Registers <see cref="IChatClient"/> based on priority: explicit <see cref="AgentCoreOptions.ChatClient"/>,
    ///     Bedrock via <see cref="AgentCoreOptions.ModelId"/>, or relies on a pre-registered IChatClient in DI.
    ///     When <see cref="AgentCoreOptions.ModelId"/> is used, also registers <see cref="IAmazonBedrockRuntime"/>
    ///     via <c>AddAWSService</c>, which resolves AWS credentials and region from the standard
    ///     provider chain (environment variables, instance profile, etc.).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Registers <see cref="ChatClientAgent"/> as a singleton, configured with the resolved IChatClient
    ///     and optional <see cref="AgentCoreOptions.AgentOptions"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Registers <see cref="AgentCoreRuntimeContextProvider"/> as a singleton for injecting
    ///     AgentCore runtime context into the MS AF pipeline.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// After calling this method, use <c>MapAgentCore</c>
    /// to map the <c>/invocations</c> and <c>/ping</c> endpoints that the AgentCore Runtime expects.
    /// </para>
    /// <para>
    /// See <see href="https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/runtime-service-contract.html">AgentCore Runtime service contract</see>
    /// for the full specification.
    /// </para>
    /// </remarks>
    /// <param name="builder">The web application builder to configure.</param>
    /// <param name="configure">
    /// An optional callback to configure <see cref="AgentCoreOptions"/>. You can set
    /// <see cref="AgentCoreOptions.ModelId"/> for Bedrock, provide an explicit
    /// <see cref="AgentCoreOptions.ChatClient"/>, or rely on a pre-registered IChatClient in DI.
    /// </param>
    /// <returns>The <see cref="WebApplicationBuilder"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.AddAgentCore(options =>
    /// {
    ///     options.ModelId = "anthropic.claude-sonnet-4-20250514-v1:0";
    /// });
    /// </code>
    /// </example>
    public static WebApplicationBuilder AddAgentCore(this WebApplicationBuilder builder, Action<AgentCoreOptions>? configure = null)
    {
        var options = new AgentCoreOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        using var loggerFactory = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            logging.AddConsole();
        });
        var logger = loggerFactory.CreateLogger("AWS.AgentCore.Hosting");

        // Register user-agent telemetry globally — applies to ALL AWS SDK clients in the process,
        // including those the user registers independently.
        UserAgentTelemetry.Initialize();

        // Only set the port explicitly if ASPNETCORE_URLS is not already configured
        // (e.g., by Aspire or another orchestrator) AND we're not running under Aspire's
        // managed mode. In production on AgentCore Runtime, neither will be set, so we
        // default to port 8080 per the service contract.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Constants.AspireManagedEnvironmentVariable)))
        {
            logger.LogDebug("Configuring default listen port");
            builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");
        }
        else
        {
            logger.LogDebug("Skipping port configuration — ASPNETCORE_URLS or Aspire managed mode detected");
        }

        // The IChatClient/AIAgent .UseOpenTelemetry() wrapping below always fires so the
        // underlying activity sources emit spans/metrics. Users who want to collect this
        // telemetry set up their own OTel pipeline (e.g. via Aspire ServiceDefaults) and call
        // AddAgentCoreInstrumentation() on their TracerProviderBuilder/MeterProviderBuilder.

        // IChatClient registration (priority order).
        // The .UseOpenTelemetry() wrapping is always applied — it has near-zero overhead when
        // no listeners are subscribed, and ensures users who wire OTel separately (via
        // AddAgentCoreInstrumentation, ServiceDefaults, ADOT, etc.) get full chat-level
        // telemetry under "Experimental.Microsoft.Extensions.AI".
        if (options.ChatClient is not null)
        {
            logger.LogDebug("IChatClient: using explicit ChatClient from options");
            var client = options.ChatClient.AsBuilder()
                .UseOpenTelemetry(configure: cfg => cfg.EnableSensitiveData = options.EnableSensitiveTelemetryData)
                .Build();
            builder.Services.AddSingleton<IChatClient>(client);
        }
        else if (!string.IsNullOrWhiteSpace(options.ModelId))
        {
            logger.LogDebug("IChatClient: using Bedrock model via ModelId option");
            builder.Services.TryAddAWSService<IAmazonBedrockRuntime>();
            builder.Services.TryAddSingleton<IChatClient>(sp =>
            {
                var bedrockClient = sp.GetRequiredService<IAmazonBedrockRuntime>();
                return bedrockClient.AsIChatClient(options.ModelId)
                    .AsBuilder()
                    .UseOpenTelemetry(configure: cfg => cfg.EnableSensitiveData = options.EnableSensitiveTelemetryData)
                    .Build();
            });
        }
        else
        {
            logger.LogDebug("IChatClient: no ChatClient or ModelId provided — expecting pre-registered IChatClient in DI");
        }

        // Register AgentCoreRuntimeContextProvider (AIContextProvider)
        builder.Services.AddSingleton<AgentCoreRuntimeContextProvider>();

        // Register IAmazonBedrockAgentCore for Memory operations.
        // When the service endpoint is set via environment variables, route all AgentCore SDK calls
        // to the specified endpoint (e.g., a local Memory Emulator).
        var serviceEndpoint = Environment.GetEnvironmentVariable(Constants.ServiceEndpointEnvironmentVariable);
        if (!string.IsNullOrEmpty(serviceEndpoint))
        {
            logger.LogDebug("AgentCore SDK client: routing to local endpoint via {EnvVar} environment variable", Constants.ServiceEndpointEnvironmentVariable);
            builder.Services.TryAddSingleton<IAmazonBedrockAgentCore>(_ =>
                new AmazonBedrockAgentCoreClient(
                    new Amazon.Runtime.AnonymousAWSCredentials(),
                    new AmazonBedrockAgentCoreConfig
                    {
                        ServiceURL = serviceEndpoint
                    }));
        }
        else
        {
            logger.LogDebug("AgentCore SDK client: using default AWS endpoint resolution");
            builder.Services.TryAddAWSService<IAmazonBedrockAgentCore>();
        }


        // Register AgentCoreMemoryProvider
        builder.Services.AddSingleton<AgentCoreMemoryProvider>();

        // Register ChatClientAgent (the raw inner agent — no OTEL wrapper or ConfigureAgent
        // middleware applied, so users can inject the concrete type and call ChatClientAgent-specific APIs).
        //
        // Trade-off: Resolving ChatClientAgent bypasses any ConfigureAgent middleware AND the
        // agent-level OpenTelemetry instrumentation (invoke_agent / execute_tool spans,
        // agent_framework.function.invocation.duration metric).
        // Chat-level telemetry (chat span, gen_ai.client.* metrics) still emits because
        // IChatClient is wrapped at registration time.
        // For full instrumentation and middleware support, inject AIAgent instead.
        builder.Services.AddSingleton<ChatClientAgent>(sp =>
        {
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient is null)
            {
                throw new InvalidOperationException(
                    "No IChatClient is registered. Provide one via: " +
                    "options.ChatClient = myClient, " +
                    "options.ModelId = \"model-id\" (for Bedrock), " +
                    "or register IChatClient in DI before calling AddAgentCore().");
            }

            var agentOptions = options.AgentOptions ?? new ChatClientAgentOptions();

            // Wire the Memory provider as the ChatHistoryProvider
            var memoryProvider = sp.GetRequiredService<AgentCoreMemoryProvider>();
            // Only set the memory provider if the user hasn't configured their own ChatHistoryProvider
            agentOptions.ChatHistoryProvider ??= memoryProvider;

            return new ChatClientAgent(chatClient, agentOptions);
        });

        // Register AIAgent (the public-facing agent, decorated with middleware and OpenTelemetry).
        // Wraps the inner ChatClientAgent with .UseOpenTelemetry() so MS Agent Framework's
        // agent-level activities (invoke_agent, execute_tool) and metrics
        // (agent_framework.function.invocation.duration) are emitted under
        // "Experimental.Microsoft.Agents.AI".
        builder.Services.AddSingleton<AIAgent>(sp =>
        {
            var chatClientAgent = sp.GetRequiredService<ChatClientAgent>();

            AIAgent configuredAgent = options.ConfigureAgent is not null
                ? options.ConfigureAgent(chatClientAgent)
                : chatClientAgent;

            // Always wrap with .UseOpenTelemetry() — near-zero cost when no listeners are
            // subscribed, and ensures users who wire OTel separately get full agent-level
            // telemetry under "Experimental.Microsoft.Agents.AI".
            return configuredAgent.AsBuilder()
                .UseOpenTelemetry(configure: cfg => cfg.EnableSensitiveData = options.EnableSensitiveTelemetryData)
                .Build();
        });

        return builder;
    }

}

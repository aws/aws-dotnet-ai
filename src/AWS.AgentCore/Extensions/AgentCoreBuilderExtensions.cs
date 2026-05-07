// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockRuntime;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.AI;

namespace AWS.AgentCore.Extensions;

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
    /// After calling this method, use <see cref="AgentCoreEndpointExtensions.MapAgentCore{TRequest}"/>
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

        builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");

        // IChatClient registration (priority order)
        if (options.ChatClient is not null)
        {
            // Explicit client takes highest priority
            builder.Services.AddSingleton<IChatClient>(options.ChatClient);
        }
        else if (!string.IsNullOrWhiteSpace(options.ModelId))
        {
            // Bedrock fallback when ModelId is provided
            builder.Services.TryAddAWSService<IAmazonBedrockRuntime>();
            builder.Services.TryAddSingleton<IChatClient>(sp =>
            {
                var bedrockClient = sp.GetRequiredService<IAmazonBedrockRuntime>();
                return bedrockClient.AsIChatClient(options.ModelId);
            });
        }
        // else: user must have pre-registered IChatClient in DI, or resolution will fail

        // Register AgentCoreRuntimeContextProvider (AIContextProvider)
        builder.Services.AddSingleton<AgentCoreRuntimeContextProvider>();

        // Register AIAgent (may be a ChatClientAgent or a middleware-decorated agent)
        builder.Services.AddSingleton<AIAgent>(sp =>
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
            var agent = new ChatClientAgent(chatClient, agentOptions);

            if (options.ConfigureAgent is not null)
            {
                return options.ConfigureAgent(agent);
            }

            return agent;
        });

        // Also register ChatClientAgent for users who need the concrete type (when no middleware is applied)
        builder.Services.AddSingleton<ChatClientAgent>(sp =>
        {
            var aiAgent = sp.GetRequiredService<AIAgent>();
            if (aiAgent is ChatClientAgent chatClientAgent)
            {
                return chatClientAgent;
            }

            throw new InvalidOperationException(
                "Cannot resolve ChatClientAgent because ConfigureAgent was used to decorate the agent with middleware. " +
                "Use AIAgent instead, which supports the full middleware pipeline.");
        });

        return builder;
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockRuntime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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
    ///     Registers <see cref="IAmazonBedrockRuntime"/> via
    ///     <c>AddAWSService</c>, which resolves AWS credentials and region from the standard
    ///     provider chain (environment variables, instance profile, etc.).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     Registers <see cref="IChatClient"/> as a singleton backed by Amazon Bedrock, using the
    ///     model specified in <see cref="AgentCoreOptions.ModelId"/>.
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
    /// A callback to configure <see cref="AgentCoreOptions"/>. You must set
    /// <see cref="AgentCoreOptions.ModelId"/> to a valid Bedrock model ID; an
    /// <see cref="ArgumentException"/> is thrown if it is not provided.
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

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new ArgumentException(
                $"{nameof(AgentCoreOptions.ModelId)} is required. " +
                "Set it via the configure callback: builder.AddAgentCore(options => options.ModelId = \"your-model-id\");",
                nameof(configure));
        }

        builder.Services.AddSingleton(options);

        builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");

        builder.Services.AddAWSService<IAmazonBedrockRuntime>();
        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            var bedrockClient = sp.GetRequiredService<IAmazonBedrockRuntime>();
            return bedrockClient.AsIChatClient(options.ModelId);
        });

        return builder;
    }
}

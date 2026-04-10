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
    /// Adds AgentCore services to the application, including the Bedrock chat client
    /// and port configuration for the AgentCore Runtime service contract.
    /// </summary>
    /// <remarks>
    /// See <see href="https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/runtime-service-contract.html">AgentCore Runtime service contract</see>.
    /// </remarks>
    public static WebApplicationBuilder AddAgentCore(this WebApplicationBuilder builder, Action<AgentCoreOptions>? configure = null)
    {
        var options = new AgentCoreOptions();
        configure?.Invoke(options);

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

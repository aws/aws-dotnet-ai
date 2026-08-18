// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AWS.Bedrock.MAG.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Adds Bedrock Guardrails policy, PII sanitization, and CloudWatch audit to an MCP server. Mirrors the
    /// toolkit's <c>.WithGovernance(...)</c> and composes with it.
    /// </summary>
    public static class BedrockGovernanceMcpServerBuilderExtensions
    {
        /// <summary>Adds the Bedrock governance backends to an MCP server builder.</summary>
        public static IMcpServerBuilder WithBedrockGovernance(
            this IMcpServerBuilder builder,
            Action<BedrockGovernanceOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(configure);

            var options = BedrockGovernanceServiceCollectionExtensions.AddShared(builder.Services, configure);

            if (options.EnablePiiSanitization)
            {
                builder.Services.AddSingleton(_ => new BedrockGuardrailsSanitizer(options.Sanitization));

                // Registered after the toolkit's post-configure so Bedrock sanitization runs on
                // already-scrubbed text. The setup resolves the optional audit emitter from the kernel.
                builder.Services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, GovernedBedrockMcpServerOptionsSetup>());
            }

            return builder;
        }
    }
}

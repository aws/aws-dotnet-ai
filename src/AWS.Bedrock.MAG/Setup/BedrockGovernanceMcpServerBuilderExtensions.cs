// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Mcp;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

// Placed in the DI namespace so the extension method is discoverable wherever the MCP server builder is in
// scope, without an extra using for AWS.Bedrock.MAG.
namespace Microsoft.Extensions.DependencyInjection
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

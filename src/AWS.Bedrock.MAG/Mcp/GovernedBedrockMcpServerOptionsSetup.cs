// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AgentGovernance;
using AgentGovernance.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace AWS.Bedrock.MAG.Mcp
{
    /// <summary>
    /// Wraps every registered MCP tool in a <see cref="GovernedBedrockMcpServerTool"/> so tool output is
    /// sanitized. Mirrors the toolkit's post-configure setup; registered after the toolkit's so Bedrock
    /// PII sanitization runs on already-scrubbed text.
    /// </summary>
    internal sealed class GovernedBedrockMcpServerOptionsSetup : IPostConfigureOptions<McpServerOptions>
    {
        private readonly BedrockGuardrailsSanitizer _sanitizer;
        private readonly AuditEmitter? _audit;

        // Resolves the audit emitter from the DI GovernanceKernel when one is present, so redactions reach
        // the audit sink. Constructed by concrete type so TryAddEnumerable can dedupe it.
        public GovernedBedrockMcpServerOptionsSetup(BedrockGuardrailsSanitizer sanitizer, IServiceProvider services)
        {
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _audit = services?.GetService<GovernanceKernel>()?.AuditEmitter;
        }

        public void PostConfigure(string? name, McpServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.ToolCollection is null || options.ToolCollection.Count == 0)
            {
                return;
            }

            var governed = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (var tool in options.ToolCollection)
            {
                governed.TryAdd(new GovernedBedrockMcpServerTool(tool, _sanitizer, _audit));
            }

            options.ToolCollection = governed;
        }
    }
}

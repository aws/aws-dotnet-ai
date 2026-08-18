// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgentGovernance.Audit;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AWS.Bedrock.MAG.Mcp
{
    /// <summary>
    /// Decorates an <see cref="McpServerTool"/> so its output passes through Bedrock Guardrails PII
    /// sanitization. Mirrors the toolkit's GovernedMcpServerTool: run the inner tool, then rebuild the
    /// result with any text blocks masked or blocked. Emits a governance event when it redacts.
    /// </summary>
    internal sealed class GovernedBedrockMcpServerTool : McpServerTool
    {
        private readonly McpServerTool _inner;
        private readonly BedrockGuardrailsSanitizer _sanitizer;
        private readonly AuditEmitter? _audit;

        public GovernedBedrockMcpServerTool(McpServerTool inner, BedrockGuardrailsSanitizer sanitizer, AuditEmitter? audit = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _audit = audit;
        }

        public override Tool ProtocolTool => _inner.ProtocolTool;

        public override IReadOnlyList<object> Metadata => _inner.Metadata;

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var result = await _inner.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Content is null || result.Content.Count == 0)
            {
                return result;
            }

            var modified = false;
            var blocked = false;
            var redactedTypes = new HashSet<string>(StringComparer.Ordinal);
            var blocks = new List<ContentBlock>(result.Content.Count);

            foreach (var block in result.Content)
            {
                if (block is TextContentBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    var sanitized = await _sanitizer.SanitizeAsync(textBlock.Text, cancellationToken).ConfigureAwait(false);
                    if (sanitized.Modified)
                    {
                        modified = true;
                        blocked |= sanitized.Blocked;
                        foreach (var type in sanitized.RedactedTypes)
                        {
                            redactedTypes.Add(type);
                        }

                        blocks.Add(new TextContentBlock
                        {
                            Text = sanitized.Text,
                            Annotations = textBlock.Annotations,
                            Meta = textBlock.Meta
                        });
                        continue;
                    }
                }

                blocks.Add(block);
            }

            if (!modified)
            {
                return result;
            }

            EmitRedaction(redactedTypes, blocked);

            // Only text blocks are sanitized, matching the toolkit's own sanitizer. StructuredContent is
            // passed through unchanged; sanitizing arbitrary structured JSON through a guardrail is a
            // post-v1 follow-up. Callers returning PII in StructuredContent should mirror it as text.
            return new CallToolResult
            {
                Content = blocks,
                StructuredContent = result.StructuredContent,
                IsError = result.IsError
            };
        }

        private void EmitRedaction(IReadOnlyCollection<string> redactedTypes, bool blocked)
        {
            if (_audit is null)
            {
                return;
            }

            var toolName = _inner.ProtocolTool.Name;
            _audit.Emit(new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = $"mcp-tool:{toolName}",
                SessionId = "mcp-response-sanitization",
                PolicyName = "bedrock-guardrails-pii",
                Data =
                {
                    ["kind"] = "pii_redaction",
                    ["tool"] = toolName,
                    ["entities"] = string.Join(",", redactedTypes),
                    ["blocked"] = blocked
                }
            });
        }
    }
}

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

            void Track(SanitizationResult sanitized)
            {
                modified = true;
                blocked |= sanitized.Blocked;
                foreach (var type in sanitized.RedactedTypes)
                {
                    redactedTypes.Add(type);
                }
            }

            foreach (var block in result.Content)
            {
                // Text carried directly in a text block.
                if (block is TextContentBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
                {
                    var sanitized = await _sanitizer.SanitizeAsync(textBlock.Text, cancellationToken).ConfigureAwait(false);
                    if (sanitized.Modified)
                    {
                        Track(sanitized);
                        blocks.Add(new TextContentBlock
                        {
                            Text = sanitized.Text,
                            Annotations = textBlock.Annotations,
                            Meta = textBlock.Meta
                        });
                        continue;
                    }
                }
                // Text carried inside an embedded text resource (e.g. a returned file); scrub it too so PII
                // in a resource isn't a fail-open bypass of the text path above.
                else if (block is EmbeddedResourceBlock { Resource: TextResourceContents trc } erb
                    && !string.IsNullOrWhiteSpace(trc.Text))
                {
                    var sanitized = await _sanitizer.SanitizeAsync(trc.Text, cancellationToken).ConfigureAwait(false);
                    if (sanitized.Modified)
                    {
                        Track(sanitized);
                        blocks.Add(new EmbeddedResourceBlock
                        {
                            Resource = new TextResourceContents
                            {
                                Text = sanitized.Text,
                                Uri = trc.Uri,
                                MimeType = trc.MimeType,
                                Meta = trc.Meta
                            },
                            Annotations = erb.Annotations,
                            Meta = erb.Meta
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

            EmitInterventionEvent(redactedTypes, blocked);

            // Scrubbing covers text blocks and embedded text resources. Not scrubbed (documented v1 limits):
            // StructuredContent (sanitizing arbitrary structured JSON is a post-v1 follow-up; callers with PII
            // there should mirror it as text), non-text content (images/audio/blob resources), and nested
            // ToolResultContentBlock content. Meta/IsError/StructuredContent are carried through unchanged.
            return new CallToolResult
            {
                Content = blocks,
                StructuredContent = result.StructuredContent,
                IsError = result.IsError,
                Meta = result.Meta
            };
        }

        private void EmitInterventionEvent(IReadOnlyCollection<string> redactedTypes, bool blocked)
        {
            if (_audit is null)
            {
                return;
            }

            // An intervention is either a PII redaction (entities enumerated) or a non-PII policy such as a
            // content/topic/word filter (no entities). Classify the audit event by what actually fired instead
            // of always labeling it a PII redaction, which the sanitizer explicitly supports for non-PII cases.
            var isPii = redactedTypes.Count > 0;
            var toolName = _inner.ProtocolTool.Name;
            _audit.Emit(new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = $"mcp-tool:{toolName}",
                SessionId = "mcp-response-sanitization",
                PolicyName = isPii ? "bedrock-guardrails-pii" : "bedrock-guardrails",
                Data =
                {
                    ["kind"] = isPii ? "pii_redaction" : "guardrail_intervention",
                    ["tool"] = toolName,
                    ["entities"] = string.Join(",", redactedTypes),
                    ["blocked"] = blocked
                }
            });
        }
    }
}

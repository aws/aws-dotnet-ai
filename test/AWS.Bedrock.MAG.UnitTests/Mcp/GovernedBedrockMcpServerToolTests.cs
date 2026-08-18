// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Mcp;
using ModelContextProtocol.Protocol;
using Moq;
using Xunit;
using ContentBlock = ModelContextProtocol.Protocol.ContentBlock;

namespace AWS.Bedrock.MAG.UnitTests.Mcp
{
    public class GovernedBedrockMcpServerToolTests
    {
        private static IAmazonBedrockRuntime BedrockReturning(ApplyGuardrailResponse response)
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            return mock.Object;
        }

        private static ApplyGuardrailResponse Redacting(string maskedText, string piiType) => new()
        {
            Action = GuardrailAction.GUARDRAIL_INTERVENED,
            Outputs = new List<GuardrailOutputContent> { new() { Text = maskedText } },
            Assessments = new List<GuardrailAssessment>
            {
                new GuardrailAssessment
                {
                    SensitiveInformationPolicy = new GuardrailSensitiveInformationPolicyAssessment
                    {
                        PiiEntities = new List<GuardrailPiiEntityFilter> { new() { Type = new GuardrailPiiEntityType(piiType) } }
                    }
                }
            }
        };

        private static BedrockGuardrailsSanitizer Sanitizer(IAmazonBedrockRuntime client)
            => new(new BedrockSanitizationOptions { GuardrailId = "gr-test" }, client);

        private static CallToolResult TextResult(string text)
            => new() { Content = new List<ContentBlock> { new TextContentBlock { Text = text } } };

        [Fact]
        public async Task Passes_text_through_unchanged_when_no_pii()
        {
            var inner = new StubTool("lookup", TextResult("nothing sensitive"));
            var sanitizer = Sanitizer(BedrockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }));
            var tool = new GovernedBedrockMcpServerTool(inner, sanitizer);

            var result = await tool.InvokeAsync(McpTest.Request());

            var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            Assert.Equal("nothing sensitive", block.Text);
        }

        [Fact]
        public async Task Redacts_pii_in_text_blocks()
        {
            var inner = new StubTool("lookup_patient", TextResult("SSN 123-45-6789"));
            var sanitizer = Sanitizer(BedrockReturning(Redacting("SSN {US_SSN}", "US_SSN")));
            var tool = new GovernedBedrockMcpServerTool(inner, sanitizer);

            var result = await tool.InvokeAsync(McpTest.Request());

            var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            Assert.Equal("SSN {US_SSN}", block.Text);
            Assert.DoesNotContain("123-45-6789", block.Text);
        }

        [Fact]
        public async Task Emits_governance_event_on_redaction()
        {
            var inner = new StubTool("lookup_patient", TextResult("SSN 123-45-6789"));
            var sanitizer = Sanitizer(BedrockReturning(Redacting("SSN {US_SSN}", "US_SSN")));
            var emitter = new AuditEmitter();
            var captured = new List<GovernanceEvent>();
            emitter.OnAll(captured.Add);

            var tool = new GovernedBedrockMcpServerTool(inner, sanitizer, emitter);
            await tool.InvokeAsync(McpTest.Request());

            var evt = Assert.Single(captured);
            Assert.Equal(GovernanceEventType.PolicyViolation, evt.Type);
            Assert.Equal("pii_redaction", evt.Data["kind"]);
            Assert.Contains("US_SSN", evt.Data["entities"]!.ToString());
        }

        [Fact]
        public async Task Sanitizes_multiple_text_blocks_and_preserves_non_text_blocks()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.Is<ApplyGuardrailRequest>(r => r.Content[0].Text.Text.Contains("123-45-6789")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Redacting("SSN {US_SSN}", "US_SSN"));
            mock.Setup(c => c.ApplyGuardrailAsync(It.Is<ApplyGuardrailRequest>(r => !r.Content[0].Text.Text.Contains("123-45-6789")), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApplyGuardrailResponse { Action = GuardrailAction.NONE });
            var sanitizer = new BedrockGuardrailsSanitizer(new BedrockSanitizationOptions { GuardrailId = "gr-test" }, mock.Object);

            var inner = new StubTool("lookup", new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    new TextContentBlock { Text = "SSN 123-45-6789" },
                    ImageContentBlock.FromBytes(new byte[] { 1, 2, 3 }, "image/png"),
                    new TextContentBlock { Text = "nothing sensitive" }
                }
            });
            var tool = new GovernedBedrockMcpServerTool(inner, sanitizer);

            var result = await tool.InvokeAsync(McpTest.Request());

            Assert.Equal(3, result.Content.Count);
            Assert.Equal("SSN {US_SSN}", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
            Assert.IsType<ImageContentBlock>(result.Content[1]);
            Assert.Equal("nothing sensitive", Assert.IsType<TextContentBlock>(result.Content[2]).Text);
        }

        [Fact]
        public async Task Does_not_emit_when_no_redaction()
        {
            var inner = new StubTool("lookup", TextResult("nothing sensitive"));
            var sanitizer = Sanitizer(BedrockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }));
            var emitter = new AuditEmitter();
            var captured = new List<GovernanceEvent>();
            emitter.OnAll(captured.Add);

            var tool = new GovernedBedrockMcpServerTool(inner, sanitizer, emitter);
            await tool.InvokeAsync(McpTest.Request());

            Assert.Empty(captured);
        }
    }
}

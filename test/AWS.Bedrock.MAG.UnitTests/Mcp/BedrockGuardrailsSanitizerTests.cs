// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Mcp;
using Moq;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Mcp
{
    public class BedrockGuardrailsSanitizerTests
    {
        private static ApplyGuardrailResponse Redacting(string maskedText, params string[] piiTypes)
        {
            var entities = new List<GuardrailPiiEntityFilter>();
            foreach (var type in piiTypes)
            {
                entities.Add(new GuardrailPiiEntityFilter { Type = new GuardrailPiiEntityType(type) });
            }

            return new ApplyGuardrailResponse
            {
                Action = GuardrailAction.GUARDRAIL_INTERVENED,
                Outputs = new List<GuardrailOutputContent> { new() { Text = maskedText } },
                Assessments = new List<GuardrailAssessment>
                {
                    new GuardrailAssessment
                    {
                        SensitiveInformationPolicy = new GuardrailSensitiveInformationPolicyAssessment { PiiEntities = entities }
                    }
                }
            };
        }

        private static Mock<IAmazonBedrockRuntime> Mock(ApplyGuardrailResponse response, Action<ApplyGuardrailRequest>? capture = null)
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApplyGuardrailRequest, CancellationToken>((r, _) => capture?.Invoke(r))
                .ReturnsAsync(response);
            return mock;
        }

        private static BedrockGuardrailsSanitizer Sanitizer(IAmazonBedrockRuntime client, bool block = false)
            => new(new BedrockSanitizationOptions { GuardrailId = "gr-test", BlockOnMatch = block }, client);

        [Fact]
        public void Ctor_throws_when_guardrail_id_missing()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            Assert.Throws<ArgumentException>(() => new BedrockGuardrailsSanitizer(new BedrockSanitizationOptions(), mock.Object));
        }

        [Fact]
        public async Task Returns_original_text_when_nothing_detected()
        {
            var sanitizer = Sanitizer(Mock(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);

            var result = await sanitizer.SanitizeAsync("nothing sensitive here");

            Assert.Equal("nothing sensitive here", result.Text);
            Assert.False(result.Modified);
            Assert.Empty(result.RedactedTypes);
        }

        [Fact]
        public async Task Redacts_using_guardrail_output_when_intervened()
        {
            var sanitizer = Sanitizer(Mock(Redacting("SSN {US_SSN}", "US_SSN")).Object);

            var result = await sanitizer.SanitizeAsync("SSN 123-45-6789");

            Assert.Equal("SSN {US_SSN}", result.Text);
            Assert.False(result.Blocked);
            Assert.True(result.Modified);
            Assert.Contains("US_SSN", result.RedactedTypes);
        }

        [Fact]
        public async Task Blocks_content_when_block_on_match()
        {
            var sanitizer = Sanitizer(Mock(Redacting("SSN {US_SSN}", "US_SSN")).Object, block: true);

            var result = await sanitizer.SanitizeAsync("SSN 123-45-6789");

            Assert.True(result.Blocked);
            Assert.True(result.Modified);
            Assert.DoesNotContain("123-45-6789", result.Text);
        }

        [Fact]
        public async Task Uses_output_source()
        {
            ApplyGuardrailRequest? captured = null;
            var sanitizer = Sanitizer(Mock(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }, r => captured = r).Object);

            await sanitizer.SanitizeAsync("some text");

            Assert.NotNull(captured);
            Assert.Equal(GuardrailContentSource.OUTPUT, captured!.Source);
        }

        [Fact]
        public async Task Propagates_bedrock_error_rather_than_leaking()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonBedrockRuntimeException("service unavailable"));
            var sanitizer = new BedrockGuardrailsSanitizer(new BedrockSanitizationOptions { GuardrailId = "gr-test" }, mock.Object);

            // A swallowed error would return the unsanitized input; it must surface instead.
            await Assert.ThrowsAsync<AmazonBedrockRuntimeException>(() => sanitizer.SanitizeAsync("SSN 123-45-6789"));
        }

        [Fact]
        public async Task Honors_non_pii_intervention_with_masked_output()
        {
            var response = new ApplyGuardrailResponse
            {
                Action = GuardrailAction.GUARDRAIL_INTERVENED,
                Outputs = new List<GuardrailOutputContent> { new() { Text = "[filtered]" } }
            };
            var sanitizer = Sanitizer(Mock(response).Object);

            var result = await sanitizer.SanitizeAsync("some banned content");

            Assert.True(result.Modified);
            Assert.Equal("[filtered]", result.Text);
        }

        [Fact]
        public async Task Fails_closed_when_intervened_but_no_masked_output()
        {
            var response = new ApplyGuardrailResponse
            {
                Action = GuardrailAction.GUARDRAIL_INTERVENED,
                Assessments = new List<GuardrailAssessment>
                {
                    new GuardrailAssessment
                    {
                        SensitiveInformationPolicy = new GuardrailSensitiveInformationPolicyAssessment
                        {
                            PiiEntities = new List<GuardrailPiiEntityFilter> { new() { Type = new GuardrailPiiEntityType("US_SSN") } }
                        }
                    }
                }
                // Outputs deliberately null: the guardrail intervened but returned no masked text.
            };
            var sanitizer = Sanitizer(Mock(response).Object);

            var result = await sanitizer.SanitizeAsync("SSN 123-45-6789");

            Assert.True(result.Blocked);
            Assert.DoesNotContain("123-45-6789", result.Text);
        }

        [Fact]
        public async Task Fails_closed_when_intervened_but_masked_output_is_whitespace_only()
        {
            // Whitespace-only masked output carries no sanitized content; treating it as valid would return an
            // effectively empty result for content the guardrail flagged as unsafe. Must fail closed.
            var response = new ApplyGuardrailResponse
            {
                Action = GuardrailAction.GUARDRAIL_INTERVENED,
                Outputs = new List<GuardrailOutputContent> { new() { Text = "   \t\n" } },
                Assessments = new List<GuardrailAssessment>
                {
                    new GuardrailAssessment
                    {
                        SensitiveInformationPolicy = new GuardrailSensitiveInformationPolicyAssessment
                        {
                            PiiEntities = new List<GuardrailPiiEntityFilter> { new() { Type = new GuardrailPiiEntityType("US_SSN") } }
                        }
                    }
                }
            };
            var sanitizer = Sanitizer(Mock(response).Object);

            var result = await sanitizer.SanitizeAsync("SSN 123-45-6789");

            Assert.True(result.Blocked);
            Assert.DoesNotContain("123-45-6789", result.Text);
        }
    }
}

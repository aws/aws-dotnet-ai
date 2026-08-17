// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AWS.AgentGovernance.Bedrock.Internal;
using Xunit;

namespace AWS.AgentGovernance.Bedrock.UnitTests
{
    public class GuardrailResponseMapperTests
    {
        [Fact]
        public void Intervened_is_true_when_guardrail_took_action()
        {
            var response = new ApplyGuardrailResponse { Action = GuardrailAction.GUARDRAIL_INTERVENED };
            Assert.True(GuardrailResponseMapper.Intervened(response));
        }

        [Fact]
        public void Intervened_is_false_when_action_is_none()
        {
            var response = new ApplyGuardrailResponse { Action = GuardrailAction.NONE };
            Assert.False(GuardrailResponseMapper.Intervened(response));
        }

        [Fact]
        public void GetRedactedText_returns_guardrail_output_when_present()
        {
            var response = new ApplyGuardrailResponse
            {
                Outputs = new List<GuardrailOutputContent> { new() { Text = "Hello {NAME}" } }
            };

            Assert.Equal("Hello {NAME}", GuardrailResponseMapper.GetRedactedText(response, "Hello Jane"));
        }

        [Fact]
        public void GetRedactedText_falls_back_to_original_when_no_outputs()
        {
            var response = new ApplyGuardrailResponse();
            Assert.Equal("Hello Jane", GuardrailResponseMapper.GetRedactedText(response, "Hello Jane"));
        }

        [Fact]
        public void GetDetectedPiiTypes_returns_distinct_types_across_assessments()
        {
            var response = new ApplyGuardrailResponse
            {
                Assessments = new List<GuardrailAssessment>
                {
                    new GuardrailAssessment
                    {
                        SensitiveInformationPolicy = new GuardrailSensitiveInformationPolicyAssessment
                        {
                            PiiEntities = new List<GuardrailPiiEntityFilter>
                            {
                                new() { Type = new GuardrailPiiEntityType("NAME") },
                                new() { Type = new GuardrailPiiEntityType("US_SSN") },
                                new() { Type = new GuardrailPiiEntityType("NAME") }
                            }
                        }
                    }
                }
            };

            var types = GuardrailResponseMapper.GetDetectedPiiTypes(response);

            Assert.Equal(2, types.Count);
            Assert.Contains("NAME", types);
            Assert.Contains("US_SSN", types);
        }

        [Fact]
        public void GetDetectedPiiTypes_is_empty_when_no_assessments()
        {
            var response = new ApplyGuardrailResponse();
            Assert.Empty(GuardrailResponseMapper.GetDetectedPiiTypes(response));
        }

        [Fact]
        public void SummarizeAssessment_includes_action_and_pii_types()
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
                            PiiEntities = new List<GuardrailPiiEntityFilter>
                            {
                                new() { Type = new GuardrailPiiEntityType("NAME") }
                            }
                        }
                    }
                }
            };

            var summary = GuardrailResponseMapper.SummarizeAssessment(response);

            Assert.Contains("action=GUARDRAIL_INTERVENED", summary);
            Assert.Contains("NAME", summary);
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AWS.Bedrock.MAG.Internal;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests
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

        [Fact]
        public void ChecksTripped_treats_a_content_finding_with_no_severity_score_as_tripped()
        {
            // Fail-safe: a flagged entry with no score must deny rather than slip through. A very high
            // threshold ensures the entry only trips because the missing score is treated as meeting it.
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    ContentFilter = new GuardrailChecksContentFilterResult
                    {
                        Results = new List<GuardrailChecksContentFilterResultEntry>
                        {
                            new() { Category = "HATE" } // SeverityScore deliberately unset (null).
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 1.0, confidenceThreshold: 1.0, out var summary);

            Assert.True(tripped);
            Assert.Contains("HATE", summary);
        }

        [Fact]
        public void ChecksTripped_treats_a_pii_finding_with_no_confidence_score_as_tripped()
        {
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    SensitiveInformation = new GuardrailChecksSensitiveInformationResult
                    {
                        Results = new List<GuardrailChecksSensitiveInformationResultEntry>
                        {
                            new() { Type = "US_SOCIAL_SECURITY_NUMBER" } // ConfidenceScore deliberately unset (null).
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 1.0, confidenceThreshold: 1.0, out var summary);

            Assert.True(tripped);
            Assert.Contains("US_SOCIAL_SECURITY_NUMBER", summary);
        }
    }
}

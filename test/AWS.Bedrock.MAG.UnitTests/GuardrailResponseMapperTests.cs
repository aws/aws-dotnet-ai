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
        public void ChecksTripped_does_not_trip_on_a_content_finding_with_no_severity_score()
        {
            // Content-filter results are PER-EVALUATED-CATEGORY: an entry is returned for every category we
            // asked to check, even for benign input, and its severity is nullable. A null severity means "no
            // finding for this category", so it must NOT trip — treating it as tripped would deny-all benign
            // traffic. (Contrast with the PII test below, where an entry only appears on an actual detection.)
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    ContentFilter = new GuardrailChecksContentFilterResult
                    {
                        Results = new List<GuardrailChecksContentFilterResultEntry>
                        {
                            new() { Category = "HATE" } // SeverityScore deliberately unset (null) => benign.
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 0.5, confidenceThreshold: 0.5, out var summary);

            Assert.False(tripped);
            Assert.Equal("no checks tripped", summary);
        }

        [Fact]
        public void ChecksTripped_does_not_trip_on_a_content_finding_below_the_severity_threshold()
        {
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    ContentFilter = new GuardrailChecksContentFilterResult
                    {
                        Results = new List<GuardrailChecksContentFilterResultEntry>
                        {
                            new() { Category = "HATE", SeverityScore = 0.2 }
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 0.5, confidenceThreshold: 0.5, out _);

            Assert.False(tripped);
        }

        [Fact]
        public void ChecksTripped_trips_on_a_content_finding_at_or_above_the_severity_threshold()
        {
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    ContentFilter = new GuardrailChecksContentFilterResult
                    {
                        Results = new List<GuardrailChecksContentFilterResultEntry>
                        {
                            new() { Category = "HATE", SeverityScore = 0.9 }
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 0.5, confidenceThreshold: 0.5, out var summary);

            Assert.True(tripped);
            Assert.Contains("HATE", summary);
        }

        [Fact]
        public void ChecksTripped_does_not_trip_on_a_prompt_attack_finding_with_no_severity_score()
        {
            // Prompt-attack results are also per-evaluated-category, so a null severity is benign, not a trip.
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    PromptAttack = new GuardrailChecksPromptAttackResult
                    {
                        Results = new List<GuardrailChecksPromptAttackResultEntry>
                        {
                            new() { Category = "PROMPT_INJECTION" } // SeverityScore deliberately unset (null).
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 0.5, confidenceThreshold: 0.5, out var summary);

            Assert.False(tripped);
            Assert.Equal("no checks tripped", summary);
        }

        [Fact]
        public void ChecksTripped_does_not_trip_on_a_prompt_attack_finding_below_the_severity_threshold()
        {
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    PromptAttack = new GuardrailChecksPromptAttackResult
                    {
                        Results = new List<GuardrailChecksPromptAttackResultEntry>
                        {
                            new() { Category = "PROMPT_INJECTION", SeverityScore = 0.2 }
                        }
                    }
                }
            };

            var tripped = GuardrailResponseMapper.ChecksTripped(response, severityThreshold: 0.5, confidenceThreshold: 0.5, out _);

            Assert.False(tripped);
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

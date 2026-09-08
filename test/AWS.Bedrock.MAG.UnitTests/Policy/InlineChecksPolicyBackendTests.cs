// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Policy;
using Moq;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Policy
{
    public class InlineChecksPolicyBackendTests
    {
        private static Mock<IAmazonBedrockRuntime> Mock(InvokeGuardrailChecksResponse response, Action<InvokeGuardrailChecksRequest>? capture = null)
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.InvokeGuardrailChecksAsync(It.IsAny<InvokeGuardrailChecksRequest>(), It.IsAny<CancellationToken>()))
                .Callback<InvokeGuardrailChecksRequest, CancellationToken>((r, _) => capture?.Invoke(r))
                .ReturnsAsync(response);
            return mock;
        }

        private static BedrockGuardrailsPolicyOptions ContentFilterOptions(double severityThreshold = 0.5)
        {
            var options = new BedrockGuardrailsPolicyOptions
            {
                InlineChecks = new GuardrailChecksOptions { SeverityThreshold = severityThreshold }
            };
            options.InlineChecks.ContentFilterCategories.Add("HATE");
            return options;
        }

        private static InvokeGuardrailChecksResponse ContentFilterResult(string category, double severity) => new()
        {
            Results = new GuardrailChecksResults
            {
                ContentFilter = new GuardrailChecksContentFilterResult
                {
                    Results = new List<GuardrailChecksContentFilterResultEntry>
                    {
                        new() { Category = category, SeverityScore = severity }
                    }
                }
            }
        };

        private static BedrockGuardrailsPolicyOptions PromptAttackOptions(double severityThreshold = 0.5)
        {
            var options = new BedrockGuardrailsPolicyOptions
            {
                InlineChecks = new GuardrailChecksOptions { SeverityThreshold = severityThreshold }
            };
            options.InlineChecks.PromptAttackCategories.Add("PROMPT_INJECTION");
            return options;
        }

        private static InvokeGuardrailChecksResponse PromptAttackResult(string category, double severity) => new()
        {
            Results = new GuardrailChecksResults
            {
                PromptAttack = new GuardrailChecksPromptAttackResult
                {
                    Results = new List<GuardrailChecksPromptAttackResultEntry>
                    {
                        new() { Category = category, SeverityScore = severity }
                    }
                }
            }
        };

        private static readonly Dictionary<string, object> Context = new() { ["tool"] = "send_email" };

        [Fact]
        public void Ctor_throws_when_neither_guardrail_nor_inline_checks_configured()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            Assert.Throws<ArgumentException>(() => new BedrockGuardrailsPolicyBackend(new BedrockGuardrailsPolicyOptions(), mock.Object));
        }

        [Fact]
        public void Ctor_accepts_inline_checks_without_a_guardrail_id()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            var backend = new BedrockGuardrailsPolicyBackend(ContentFilterOptions(), mock.Object);
            Assert.Equal("bedrock-guardrails", backend.Name);
        }

        [Fact]
        public async Task Uses_invoke_guardrail_checks_when_no_guardrail_id()
        {
            InvokeGuardrailChecksRequest? captured = null;
            var mock = Mock(new InvokeGuardrailChecksResponse { Results = new GuardrailChecksResults() }, r => captured = r);
            var backend = new BedrockGuardrailsPolicyBackend(ContentFilterOptions(), mock.Object);

            await backend.EvaluateAsync(Context);

            mock.Verify(c => c.InvokeGuardrailChecksAsync(It.IsAny<InvokeGuardrailChecksRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            mock.Verify(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.NotNull(captured);
            Assert.NotNull(captured!.Checks.ContentFilter);
            Assert.Single(captured.Checks.ContentFilter.Categories);
        }

        [Fact]
        public async Task Allows_when_no_check_trips()
        {
            var backend = new BedrockGuardrailsPolicyBackend(
                ContentFilterOptions(), Mock(new InvokeGuardrailChecksResponse { Results = new GuardrailChecksResults() }).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed);
            Assert.Null(decision.Error);
        }

        [Fact]
        public async Task Denies_when_a_check_meets_the_severity_threshold()
        {
            var backend = new BedrockGuardrailsPolicyBackend(
                ContentFilterOptions(severityThreshold: 0.5), Mock(ContentFilterResult("HATE", 0.9)).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.False(decision.Allowed);
            Assert.Contains("HATE", decision.Reason);
        }

        [Fact]
        public async Task Allows_when_score_is_below_the_severity_threshold()
        {
            var backend = new BedrockGuardrailsPolicyBackend(
                ContentFilterOptions(severityThreshold: 0.5), Mock(ContentFilterResult("HATE", 0.2)).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed);
        }

        [Fact]
        public async Task Allows_benign_content_filter_response_with_per_category_null_severity_entries()
        {
            // Regression: content-filter results are PER-EVALUATED-CATEGORY — InvokeGuardrailChecks returns an
            // entry for every category asked, even on benign input, with a null severity. Treating a null
            // severity as tripped denied ALL benign traffic. A null-severity entry (plus one below threshold)
            // must NOT deny.
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    ContentFilter = new GuardrailChecksContentFilterResult
                    {
                        Results = new List<GuardrailChecksContentFilterResultEntry>
                        {
                            new() { Category = "HATE" },                    // null severity => benign
                            new() { Category = "VIOLENCE", SeverityScore = 0.1 } // below threshold => benign
                        }
                    }
                }
            };

            var backend = new BedrockGuardrailsPolicyBackend(ContentFilterOptions(severityThreshold: 0.5), Mock(response).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed, decision.Reason);
            Assert.Null(decision.Error);
        }

        [Fact]
        public async Task Allows_benign_prompt_attack_response_with_per_category_null_severity_entries()
        {
            // Prompt-attack analog of the benign content-filter regression above.
            var response = new InvokeGuardrailChecksResponse
            {
                Results = new GuardrailChecksResults
                {
                    PromptAttack = new GuardrailChecksPromptAttackResult
                    {
                        Results = new List<GuardrailChecksPromptAttackResultEntry>
                        {
                            new() { Category = "PROMPT_INJECTION" },              // null severity => benign
                            new() { Category = "PROMPT_INJECTION", SeverityScore = 0.1 } // below threshold => benign
                        }
                    }
                }
            };

            var backend = new BedrockGuardrailsPolicyBackend(PromptAttackOptions(severityThreshold: 0.5), Mock(response).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed, decision.Reason);
            Assert.Null(decision.Error);
        }

        [Fact]
        public async Task Emits_prompt_attack_config_and_denies_on_severity()
        {
            InvokeGuardrailChecksRequest? captured = null;
            var mock = Mock(PromptAttackResult("PROMPT_INJECTION", 0.9), r => captured = r);
            var backend = new BedrockGuardrailsPolicyBackend(PromptAttackOptions(severityThreshold: 0.5), mock.Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.NotNull(captured);
            Assert.NotNull(captured!.Checks.PromptAttack);
            Assert.Single(captured.Checks.PromptAttack.Categories);
            Assert.False(decision.Allowed);
            Assert.Contains("PROMPT_INJECTION", decision.Reason);
        }

        [Theory]
        [InlineData(-0.1)]
        [InlineData(1.1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Threshold_setters_reject_out_of_range_or_non_finite_values(double bad)
        {
            // An unenforced threshold > 1 (or NaN) can never be met and would silently allow every detection.
            Assert.Throws<ArgumentOutOfRangeException>(() => new GuardrailChecksOptions { SeverityThreshold = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => new GuardrailChecksOptions { ConfidenceThreshold = bad });
        }

        [Fact]
        public async Task Throws_when_inline_checks_are_emptied_after_construction()
        {
            // The ctor validated HasAnyCheck, but the config and its lists are mutable. Emptying them after
            // construction leaves nothing to evaluate; that must surface as a loud InvalidOperationException,
            // NOT be swallowed by the fail-open catch into an allow-all. FailClosed=false makes the point: a
            // fail-open backend must still refuse to evaluate an empty config rather than allow everything.
            var options = ContentFilterOptions();
            options.FailClosed = false;
            var mock = Mock(new InvokeGuardrailChecksResponse { Results = new GuardrailChecksResults() });
            var backend = new BedrockGuardrailsPolicyBackend(options, mock.Object);

            options.InlineChecks!.ContentFilterCategories.Clear();

            await Assert.ThrowsAsync<InvalidOperationException>(() => backend.EvaluateAsync(Context));
            mock.Verify(c => c.InvokeGuardrailChecksAsync(It.IsAny<InvokeGuardrailChecksRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}

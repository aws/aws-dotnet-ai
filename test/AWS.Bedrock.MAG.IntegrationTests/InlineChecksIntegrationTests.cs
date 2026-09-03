// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading.Tasks;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using AWS.Bedrock.MAG.Policy;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>
    /// Runs the policy backend in inline-checks mode against real InvokeGuardrailChecks, i.e. with no
    /// pre-created guardrail (PR: InvokeGuardrailChecks). Deliberately does NOT join the
    /// "bedrock-integration" collection: the shared fixture provisions a real guardrail and log group, which
    /// this mode neither needs nor should require, so these tests can run with inline-check-only permissions
    /// and genuinely exercise the no-pre-created-guardrail path.
    /// </summary>
    public class InlineChecksIntegrationTests
    {
        private static BedrockGuardrailsPolicyBackend Backend()
        {
            var options = new BedrockGuardrailsPolicyOptions
            {
                Region = IntegrationConfig.Region,
                InlineChecks = new GuardrailChecksOptions { ConfidenceThreshold = 0.1 }
            };
            options.InlineChecks.SensitiveInformationEntities.Add("US_SOCIAL_SECURITY_NUMBER");
            return new BedrockGuardrailsPolicyBackend(options);
        }

        [Fact]
        public async Task Denies_when_inline_pii_check_detects_an_ssn()
        {
            var decision = await Backend().EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "lookup",
                ["arg"] = "the ssn is 123-45-6789"
            });

            Assert.False(decision.Allowed);
        }

        [Fact]
        public async Task Allows_when_inline_pii_check_finds_nothing()
        {
            var decision = await Backend().EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "lookup",
                ["arg"] = "the weather is sunny today"
            });

            Assert.True(decision.Allowed, decision.Reason);
        }
    }
}

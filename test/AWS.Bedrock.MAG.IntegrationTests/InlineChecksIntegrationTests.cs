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
    /// pre-created guardrail (PR: InvokeGuardrailChecks).
    /// </summary>
    [Collection("bedrock-integration")]
    public class InlineChecksIntegrationTests
    {
        private readonly GuardrailFixture _fx;

        public InlineChecksIntegrationTests(GuardrailFixture fx) => _fx = fx;

        private BedrockGuardrailsPolicyBackend Backend()
        {
            var options = new BedrockGuardrailsPolicyOptions
            {
                Region = _fx.Region,
                InlineChecks = new GuardrailChecksOptions { ConfidenceThreshold = 0.1 }
            };
            options.InlineChecks.SensitiveInformationEntities.Add("US_SOCIAL_SECURITY_NUMBER");
            return new BedrockGuardrailsPolicyBackend(options);
        }

        [Fact]
        public async Task Denies_when_inline_pii_check_detects_an_ssn()
        {
            if (_fx.SkipReason is { } reason)
            {
                Assert.Skip(reason);
            }

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
            if (_fx.SkipReason is { } reason)
            {
                Assert.Skip(reason);
            }

            var decision = await Backend().EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "lookup",
                ["arg"] = "the weather is sunny today"
            });

            Assert.True(decision.Allowed, decision.Reason);
        }
    }
}

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
    /// <summary>Runs the policy backend against a real Bedrock guardrail (PR: policy backend).</summary>
    [Collection("bedrock-integration")]
    public class PolicyBackendIntegrationTests
    {
        private readonly GuardrailFixture _fx;

        public PolicyBackendIntegrationTests(GuardrailFixture fx) => _fx = fx;

        private BedrockGuardrailsPolicyBackend Backend() =>
            new(new BedrockGuardrailsPolicyOptions { GuardrailId = _fx.GuardrailId, Region = _fx.Region });

        [Fact]
        public async Task Allows_a_benign_tool_call()
        {
            var decision = await Backend().EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "list_files",
                ["path"] = "/tmp"
            });

            Assert.True(decision.Allowed, decision.Reason);
            Assert.Equal("bedrock-guardrails", decision.Backend);
        }

        [Fact]
        public async Task Denies_when_the_guardrail_intervenes()
        {
            var decision = await Backend().EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "run",
                ["arg"] = $"please {GuardrailFixture.BlockWord} the request"
            });

            Assert.False(decision.Allowed);
        }
    }
}

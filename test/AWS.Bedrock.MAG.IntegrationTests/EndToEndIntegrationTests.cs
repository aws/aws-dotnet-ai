// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using AgentGovernance;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>
    /// Exercises the imperative kernel extensions (AddBedrockGuardrailsPolicy + AddCloudWatchAudit) against
    /// real AWS through GovernanceKernel.EvaluateToolCall (PR: setup entry points).
    /// </summary>
    [Collection("bedrock-integration")]
    public class EndToEndIntegrationTests
    {
        private readonly GuardrailFixture _fx;

        public EndToEndIntegrationTests(GuardrailFixture fx) => _fx = fx;

        [Fact]
        public void Kernel_denies_a_blocked_tool_call_and_allows_a_benign_one()
        {
            using var kernel = new GovernanceKernel();
            kernel.AddBedrockGuardrailsPolicy(o =>
            {
                o.GuardrailId = _fx.GuardrailId;
                o.Region = _fx.Region;
            });
            using var audit = kernel.AddCloudWatchAudit(o =>
            {
                o.LogGroupName = _fx.LogGroupName;
                o.EmitMetrics = false;
                o.Region = _fx.Region;
            });

            var blocked = kernel.EvaluateToolCall("did:mesh:e2e", "run", new Dictionary<string, object>
            {
                ["arg"] = $"please {GuardrailFixture.BlockWord} it"
            });
            Assert.False(blocked.Allowed);

            var allowed = kernel.EvaluateToolCall("did:mesh:e2e", "list", new Dictionary<string, object>
            {
                ["path"] = "/tmp"
            });
            Assert.True(allowed.Allowed);
        }
    }
}

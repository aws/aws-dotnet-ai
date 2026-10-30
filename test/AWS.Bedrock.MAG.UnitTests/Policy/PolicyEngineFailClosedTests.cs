// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Threading;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AgentGovernance.Policy;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Policy;
using Moq;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Policy
{
    /// <summary>
    /// Drives the backend through the real toolkit <see cref="PolicyEngine"/> to prove the fail-closed
    /// contract end to end: the engine denies when a decision carries an Error or is not allowed.
    /// </summary>
    public class PolicyEngineFailClosedTests
    {
        private static PolicyEngine EngineWith(IAmazonBedrockRuntime client, bool failClosed = true)
        {
            var engine = new PolicyEngine();
            engine.AddExternalBackend(new BedrockGuardrailsPolicyBackend(
                new BedrockGuardrailsPolicyOptions { GuardrailId = "gr-test", FailClosed = failClosed }, client));
            return engine;
        }

        private static Mock<IAmazonBedrockRuntime> Returning(ApplyGuardrailResponse response)
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);
            return mock;
        }

        private static Mock<IAmazonBedrockRuntime> Throwing()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonBedrockRuntimeException("boom"));
            return mock;
        }

        private static readonly Dictionary<string, object> Context = new() { ["tool"] = "send_email" };

        [Fact]
        public void Engine_allows_when_guardrail_returns_none()
        {
            var engine = EngineWith(Returning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);

            var decision = engine.Evaluate("did:mesh:test-agent", Context);

            Assert.True(decision.Allowed);
        }

        [Fact]
        public void Engine_denies_when_guardrail_intervenes()
        {
            var engine = EngineWith(Returning(new ApplyGuardrailResponse { Action = GuardrailAction.GUARDRAIL_INTERVENED }).Object);

            var decision = engine.Evaluate("did:mesh:test-agent", Context);

            Assert.False(decision.Allowed);
        }

        [Fact]
        public void Engine_denies_on_error_when_fail_closed()
        {
            var engine = EngineWith(Throwing().Object, failClosed: true);

            var decision = engine.Evaluate("did:mesh:test-agent", Context);

            Assert.False(decision.Allowed);
        }

        [Fact]
        public void Engine_allows_on_error_when_fail_open()
        {
            var engine = EngineWith(Throwing().Object, failClosed: false);

            var decision = engine.Evaluate("did:mesh:test-agent", Context);

            Assert.True(decision.Allowed);
        }
    }
}

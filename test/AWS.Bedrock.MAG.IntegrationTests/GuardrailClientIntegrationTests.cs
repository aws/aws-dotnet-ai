// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using AWS.Bedrock.MAG.Internal;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>Round-trips the shared guardrail client against real Bedrock (PR: scaffold).</summary>
    [Collection("bedrock-integration")]
    public class GuardrailClientIntegrationTests : IDisposable
    {
        private readonly GuardrailFixture _fx;
        private readonly AmazonBedrockRuntimeClient _runtime;

        public GuardrailClientIntegrationTests(GuardrailFixture fx)
        {
            _fx = fx;
            _runtime = new AmazonBedrockRuntimeClient(_fx.Region);
        }

        public void Dispose() => _runtime.Dispose();

        private BedrockGuardrailClient Client() => new(_runtime);

        [Fact]
        public async Task ApplyAsync_reports_intervention_for_blocked_input()
        {
            var invocation = await Client().ApplyAsync(
                _fx.GuardrailId, _fx.GuardrailVersion, GuardrailContentSource.INPUT, $"do {GuardrailFixture.BlockWord} now");

            Assert.True(GuardrailResponseMapper.Intervened(invocation.Response));
            Assert.True(invocation.EvaluationMs > 0);
        }

        [Fact]
        public async Task ApplyAsync_reports_no_intervention_for_benign_input()
        {
            var invocation = await Client().ApplyAsync(
                _fx.GuardrailId, _fx.GuardrailVersion, GuardrailContentSource.INPUT, "list the files in the directory");

            Assert.False(GuardrailResponseMapper.Intervened(invocation.Response));
        }
    }
}

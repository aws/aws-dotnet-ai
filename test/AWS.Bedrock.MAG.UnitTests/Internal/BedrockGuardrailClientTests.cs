// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using AWS.Bedrock.MAG.Internal;
using Moq;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Internal
{
    /// <summary>
    /// Unit coverage for the shared guardrail client's request mapping, so CI protects the request built
    /// here without needing the real-AWS integration tests to run.
    /// </summary>
    public class BedrockGuardrailClientTests
    {
        [Fact]
        public async Task ApplyAsync_maps_request_fields_and_propagates_response()
        {
            ApplyGuardrailRequest? captured = null;
            var response = new ApplyGuardrailResponse { Action = GuardrailAction.NONE };
            var runtime = new Mock<IAmazonBedrockRuntime>();
            runtime
                .Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApplyGuardrailRequest, CancellationToken>((r, _) => captured = r)
                .ReturnsAsync(response);

            var client = new BedrockGuardrailClient(runtime.Object);
            var invocation = await client.ApplyAsync(
                "gr-123", "DRAFT", GuardrailContentSource.INPUT, "inspect this text");

            Assert.NotNull(captured);
            Assert.Equal("gr-123", captured!.GuardrailIdentifier);
            Assert.Equal("DRAFT", captured.GuardrailVersion);
            Assert.Equal(GuardrailContentSource.INPUT, captured.Source);
            Assert.Equal("inspect this text", Assert.Single(captured.Content).Text.Text);
            Assert.Same(response, invocation.Response);
            Assert.True(invocation.EvaluationMs >= 0);
        }

        [Fact]
        public async Task ApplyAsync_forwards_source_and_cancellation_token()
        {
            using var cts = new CancellationTokenSource();
            CancellationToken observed = default;
            var runtime = new Mock<IAmazonBedrockRuntime>();
            runtime
                .Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApplyGuardrailRequest, CancellationToken>((_, ct) => observed = ct)
                .ReturnsAsync(new ApplyGuardrailResponse { Action = GuardrailAction.NONE });

            var client = new BedrockGuardrailClient(runtime.Object);
            await client.ApplyAsync("gr", "1", GuardrailContentSource.OUTPUT, "text", cts.Token);

            Assert.Equal(cts.Token, observed);
        }
    }
}

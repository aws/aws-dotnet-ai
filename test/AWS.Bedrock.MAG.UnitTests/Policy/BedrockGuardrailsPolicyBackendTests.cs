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
    public class BedrockGuardrailsPolicyBackendTests
    {
        private static Mock<IAmazonBedrockRuntime> MockReturning(ApplyGuardrailResponse response, Action<ApplyGuardrailRequest>? capture = null)
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .Callback<ApplyGuardrailRequest, CancellationToken>((r, _) => capture?.Invoke(r))
                .ReturnsAsync(response);
            return mock;
        }

        private static Mock<IAmazonBedrockRuntime> MockThrowing()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            mock.Setup(c => c.ApplyGuardrailAsync(It.IsAny<ApplyGuardrailRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonBedrockRuntimeException("service unavailable"));
            return mock;
        }

        private static BedrockGuardrailsPolicyBackend Backend(IAmazonBedrockRuntime client, bool failClosed = true)
            => new(new BedrockGuardrailsPolicyOptions { GuardrailId = "gr-test", FailClosed = failClosed }, client);

        private static readonly Dictionary<string, object> Context = new()
        {
            ["tool"] = "file_write",
            ["path"] = "/etc/passwd"
        };

        [Fact]
        public void Name_is_bedrock_guardrails()
        {
            var backend = Backend(MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);
            Assert.Equal("bedrock-guardrails", backend.Name);
        }

        [Fact]
        public void Ctor_throws_when_guardrail_id_missing()
        {
            var mock = new Mock<IAmazonBedrockRuntime>();
            Assert.Throws<ArgumentException>(() => new BedrockGuardrailsPolicyBackend(new BedrockGuardrailsPolicyOptions(), mock.Object));
        }

        [Fact]
        public async Task EvaluateAsync_allows_when_guardrail_returns_none()
        {
            var backend = Backend(MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed);
            Assert.Equal("bedrock-guardrails", decision.Backend);
            Assert.Null(decision.Error);
        }

        [Fact]
        public async Task EvaluateAsync_denies_when_guardrail_intervenes()
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
                            PiiEntities = new List<GuardrailPiiEntityFilter> { new() { Type = new GuardrailPiiEntityType("US_SSN") } }
                        }
                    }
                }
            };
            var backend = Backend(MockReturning(response).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.False(decision.Allowed);
            Assert.Null(decision.Error);
            Assert.Contains("US_SSN", decision.Reason);
        }

        [Fact]
        public async Task EvaluateAsync_uses_input_source()
        {
            ApplyGuardrailRequest? captured = null;
            var backend = Backend(MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }, r => captured = r).Object);

            await backend.EvaluateAsync(Context);

            Assert.NotNull(captured);
            Assert.Equal(GuardrailContentSource.INPUT, captured!.Source);
            Assert.Equal("gr-test", captured.GuardrailIdentifier);
        }

        [Fact]
        public async Task Default_context_serializer_emits_json()
        {
            ApplyGuardrailRequest? captured = null;
            var backend = Backend(MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }, r => captured = r).Object);

            await backend.EvaluateAsync(Context);

            var text = captured!.Content[0].Text.Text;
            Assert.StartsWith("{", text);
            Assert.Contains("\"tool\":\"file_write\"", text);
            Assert.Contains("\"path\":\"/etc/passwd\"", text);
        }

        [Fact]
        public async Task EvaluateAsync_fail_closed_denies_and_sets_error_on_exception()
        {
            var backend = Backend(MockThrowing().Object, failClosed: true);

            var decision = await backend.EvaluateAsync(Context);

            Assert.False(decision.Allowed);
            Assert.False(string.IsNullOrWhiteSpace(decision.Error));
        }

        [Fact]
        public async Task EvaluateAsync_fail_open_allows_and_leaves_error_empty_on_exception()
        {
            var backend = Backend(MockThrowing().Object, failClosed: false);

            var decision = await backend.EvaluateAsync(Context);

            Assert.True(decision.Allowed);
            // Error MUST be empty on fail-open, or the engine's (Error || !Allowed) predicate still denies.
            Assert.True(string.IsNullOrWhiteSpace(decision.Error));
            Assert.NotNull(decision.Metadata);
            Assert.True(decision.Metadata!.ContainsKey("error"));
        }

        [Fact]
        public async Task EvaluateAsync_fails_closed_when_context_serializer_throws()
        {
            var options = new BedrockGuardrailsPolicyOptions
            {
                GuardrailId = "gr-test",
                FailClosed = true,
                ContextSerializer = _ => throw new InvalidOperationException("serializer boom")
            };
            var backend = new BedrockGuardrailsPolicyBackend(options, MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);

            var decision = await backend.EvaluateAsync(Context);

            Assert.False(decision.Allowed);
            Assert.False(string.IsNullOrWhiteSpace(decision.Error));
        }

        [Fact]
        public void Evaluate_sync_bridges_to_async()
        {
            var backend = Backend(MockReturning(new ApplyGuardrailResponse { Action = GuardrailAction.NONE }).Object);

            var decision = backend.Evaluate(Context);

            Assert.True(decision.Allowed);
        }
    }
}

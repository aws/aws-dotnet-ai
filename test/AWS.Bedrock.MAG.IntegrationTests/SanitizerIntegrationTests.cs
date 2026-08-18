// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Tasks;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using AWS.Bedrock.MAG.Mcp;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>Runs the PII sanitizer against a real Bedrock guardrail (PR: sanitizer).</summary>
    [Collection("bedrock-integration")]
    public class SanitizerIntegrationTests
    {
        private readonly GuardrailFixture _fx;

        public SanitizerIntegrationTests(GuardrailFixture fx) => _fx = fx;

        private BedrockGuardrailsSanitizer Sanitizer() =>
            new(new BedrockSanitizationOptions { GuardrailId = _fx.GuardrailId, Region = _fx.Region });

        [Fact]
        public async Task Redacts_an_ssn_in_tool_output()
        {
            if (_fx.SkipReason is { } reason)
            {
                Assert.Skip(reason);
            }

            var result = await Sanitizer().SanitizeAsync(GuardrailFixture.SsnSample);

            Assert.True(result.Modified);
            Assert.Contains(GuardrailFixture.SsnPlaceholder, result.Text);
            Assert.DoesNotContain("123-45-6789", result.Text);
        }

        [Fact]
        public async Task Leaves_clean_output_unchanged()
        {
            if (_fx.SkipReason is { } reason)
            {
                Assert.Skip(reason);
            }

            var result = await Sanitizer().SanitizeAsync("The build finished successfully.");

            Assert.False(result.Modified);
            Assert.Equal("The build finished successfully.", result.Text);
        }
    }
}

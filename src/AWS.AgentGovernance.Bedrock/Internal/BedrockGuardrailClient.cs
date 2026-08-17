// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AWS.AgentGovernance.Bedrock.Internal
{
    /// <summary>
    /// Thin wrapper over <see cref="IAmazonBedrockRuntime.ApplyGuardrailAsync"/> shared by the policy
    /// backend and the PII sanitizer. Builds the request, times the call, and returns the raw response.
    /// Deliberately does no error handling: callers decide how to fail (fail-closed policy vs.
    /// never-throw audit), so exceptions propagate here.
    /// </summary>
    internal sealed class BedrockGuardrailClient
    {
        private readonly IAmazonBedrockRuntime _client;

        public BedrockGuardrailClient(IAmazonBedrockRuntime client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Applies a guardrail to a single block of text and returns the response plus the elapsed time.
        /// </summary>
        public async Task<GuardrailInvocation> ApplyAsync(
            string guardrailId,
            string guardrailVersion,
            GuardrailContentSource source,
            string text,
            CancellationToken cancellationToken = default)
        {
            var request = new ApplyGuardrailRequest
            {
                GuardrailIdentifier = guardrailId,
                GuardrailVersion = guardrailVersion,
                Source = source,
                Content = new List<GuardrailContentBlock>
                {
                    new GuardrailContentBlock
                    {
                        Text = new GuardrailTextBlock { Text = text }
                    }
                }
            };

            var start = Stopwatch.GetTimestamp();
            var response = await _client.ApplyGuardrailAsync(request, cancellationToken).ConfigureAwait(false);
            var evaluationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            return new GuardrailInvocation(response, evaluationMs);
        }
    }

    /// <summary>A guardrail response paired with how long the round-trip took, in milliseconds.</summary>
    internal readonly record struct GuardrailInvocation(ApplyGuardrailResponse Response, double EvaluationMs);
}

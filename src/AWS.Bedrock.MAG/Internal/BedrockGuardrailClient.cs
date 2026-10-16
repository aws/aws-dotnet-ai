// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AWS.Bedrock.MAG.Internal
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

        /// <summary>
        /// Runs inline guardrail checks (InvokeGuardrailChecks) over a single block of text and returns the
        /// response plus the elapsed time. No pre-created guardrail is required.
        /// </summary>
        public async Task<GuardrailChecksInvocation> InvokeChecksAsync(
            GuardrailChecksOptions checks,
            string text,
            CancellationToken cancellationToken = default)
        {
            var config = new GuardrailChecksConfig();
            if (checks.ContentFilterCategories.Count > 0)
            {
                config.ContentFilter = new GuardrailChecksContentFilterConfig
                {
                    Categories = checks.ContentFilterCategories
                        .Select(c => new GuardrailChecksContentFilterCategoryConfig { Category = c }).ToList()
                };
            }

            if (checks.PromptAttackCategories.Count > 0)
            {
                config.PromptAttack = new GuardrailChecksPromptAttackConfig
                {
                    Categories = checks.PromptAttackCategories
                        .Select(c => new GuardrailChecksPromptAttackCategoryConfig { Category = c }).ToList()
                };
            }

            if (checks.SensitiveInformationEntities.Count > 0)
            {
                config.SensitiveInformation = new GuardrailChecksSensitiveInformationConfig
                {
                    Entities = checks.SensitiveInformationEntities
                        .Select(e => new GuardrailChecksSensitiveInformationEntityConfig { Type = e }).ToList()
                };
            }

            var request = new InvokeGuardrailChecksRequest
            {
                Checks = config,
                Messages = new List<GuardrailChecksMessage>
                {
                    new GuardrailChecksMessage
                    {
                        Role = "user",
                        Content = new List<GuardrailChecksContentBlock>
                        {
                            new GuardrailChecksContentBlock { Text = text }
                        }
                    }
                }
            };

            var start = Stopwatch.GetTimestamp();
            var response = await _client.InvokeGuardrailChecksAsync(request, cancellationToken).ConfigureAwait(false);
            var evaluationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            return new GuardrailChecksInvocation(response, evaluationMs);
        }
    }

    /// <summary>A guardrail response paired with how long the round-trip took, in milliseconds.</summary>
    internal readonly record struct GuardrailInvocation(ApplyGuardrailResponse Response, double EvaluationMs);

    /// <summary>An inline-checks response paired with how long the round-trip took, in milliseconds.</summary>
    internal readonly record struct GuardrailChecksInvocation(InvokeGuardrailChecksResponse Response, double EvaluationMs);
}

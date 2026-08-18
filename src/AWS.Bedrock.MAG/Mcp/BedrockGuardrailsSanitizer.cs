// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using AWS.Bedrock.MAG.Internal;

namespace AWS.Bedrock.MAG.Mcp
{
    /// <summary>
    /// Runs text through a Bedrock Guardrail on the output side to redact or block PII. Used by the MCP
    /// tool decorator, and usable directly to sanitize any string.
    /// </summary>
    public sealed class BedrockGuardrailsSanitizer
    {
        private const string BlockedPlaceholder = "[Content withheld: Bedrock guardrail detected sensitive information.]";

        private readonly BedrockSanitizationOptions _options;
        private readonly BedrockGuardrailClient _client;

        /// <summary>
        /// Creates a sanitizer. Pass a client for full control; otherwise one is built from
        /// <see cref="BedrockSanitizationOptions.Region"/> and
        /// <see cref="BedrockSanitizationOptions.Credentials"/>, falling back to the default chain.
        /// </summary>
        public BedrockGuardrailsSanitizer(BedrockSanitizationOptions options, IAmazonBedrockRuntime? client = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.GuardrailId))
            {
                throw new ArgumentException($"{nameof(BedrockSanitizationOptions)}.{nameof(BedrockSanitizationOptions.GuardrailId)} must be set.", nameof(options));
            }

            _client = new BedrockGuardrailClient(client ?? CreateClient(_options.Region, _options.Credentials));
        }

        /// <summary>
        /// Applies the guardrail (Source=OUTPUT) to <paramref name="text"/>. Returns the original text when
        /// nothing is detected, the masked text when redacting, or a block notice when
        /// <see cref="BedrockSanitizationOptions.BlockOnMatch"/> is set. Throws on a Bedrock/AWS error so a
        /// failure never silently leaks unsanitized output.
        /// </summary>
        public async Task<SanitizationResult> SanitizeAsync(string text, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);

            var response = (await _client
                .ApplyAsync(_options.GuardrailId!, _options.GuardrailVersion, GuardrailContentSource.OUTPUT, text, cancellationToken)
                .ConfigureAwait(false)).Response;

            if (!GuardrailResponseMapper.Intervened(response))
            {
                return new SanitizationResult { Text = text, RedactedTypes = Array.Empty<string>(), Blocked = false, Intervened = false };
            }

            var detected = GuardrailResponseMapper.GetDetectedPiiTypes(response);
            var masked = response.Outputs?.FirstOrDefault()?.Text;

            // Block explicitly, or fail closed when the guardrail intervened but returned no masked text.
            // Never fall back to the original content: intervention means it was not safe to return as-is.
            if (_options.BlockOnMatch || string.IsNullOrEmpty(masked))
            {
                return new SanitizationResult { Text = BlockedPlaceholder, RedactedTypes = detected, Blocked = true, Intervened = true };
            }

            // Honor any intervention (PII redaction or a non-PII policy such as a content or topic filter),
            // even when no PII entities were enumerated.
            return new SanitizationResult { Text = masked!, RedactedTypes = detected, Blocked = false, Intervened = true };
        }

        private static IAmazonBedrockRuntime CreateClient(RegionEndpoint? region, AWSCredentials? credentials)
        {
            if (credentials is not null)
            {
                return region is null
                    ? new AmazonBedrockRuntimeClient(credentials)
                    : new AmazonBedrockRuntimeClient(credentials, region);
            }

            return region is null ? new AmazonBedrockRuntimeClient() : new AmazonBedrockRuntimeClient(region);
        }
    }

    /// <summary>The outcome of sanitizing a single block of text.</summary>
    public sealed class SanitizationResult
    {
        /// <summary>The text to return: unchanged, masked, or a block notice.</summary>
        public required string Text { get; init; }

        /// <summary>The PII entity types the guardrail detected (empty when nothing was detected).</summary>
        public required IReadOnlyList<string> RedactedTypes { get; init; }

        /// <summary>True when the content was withheld rather than masked.</summary>
        public bool Blocked { get; init; }

        /// <summary>True when the guardrail took action (masked, blocked, or any non-PII policy intervention).</summary>
        public bool Intervened { get; init; }

        /// <summary>True when the returned text differs from the input and should replace it.</summary>
        public bool Modified => Blocked || Intervened;
    }
}

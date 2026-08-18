// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Configures Bedrock Guardrails PII sanitization of MCP tool output.
    /// </summary>
    public sealed class BedrockSanitizationOptions
    {
        /// <summary>
        /// The guardrail identifier used to sanitize tool output. When unset, the setup entry points fall
        /// back to the policy backend's guardrail.
        /// </summary>
        public string? GuardrailId { get; set; }

        /// <summary>The guardrail version to apply. Defaults to the mutable working draft.</summary>
        public string GuardrailVersion { get; set; } = "DRAFT";

        /// <summary>
        /// When false (default), detected PII is redacted (ANONYMIZE) and the masked text is returned.
        /// When true, a tool result containing PII is replaced with a block notice instead.
        /// </summary>
        public bool BlockOnMatch { get; set; }

        /// <summary>The region to create the Bedrock client in. Null uses the default credential/region chain.</summary>
        public RegionEndpoint? Region { get; set; }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Amazon;
using Amazon.Runtime;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Configures the Bedrock Guardrails policy backend.
    /// </summary>
    public sealed class BedrockGuardrailsPolicyOptions
    {
        /// <summary>The guardrail identifier (ID or ARN) to evaluate tool-call context against. Required.</summary>
        public string? GuardrailId { get; set; }

        /// <summary>The guardrail version to apply. Defaults to the mutable working draft.</summary>
        public string GuardrailVersion { get; set; } = "DRAFT";

        /// <summary>
        /// Serializes the tool-call context (tool name and arguments the toolkit passes) into the text
        /// handed to the guardrail. Defaults to a compact JSON object, matching the toolkit's OPA and
        /// Cedar backends. Override with a prose projection when leaning on topic or intent policies that
        /// an ML guardrail scores better against free text.
        /// </summary>
        public Func<IReadOnlyDictionary<string, object>, string>? ContextSerializer { get; set; }

        /// <summary>The region to create the Bedrock client in. Null uses the default credential/region chain.</summary>
        public RegionEndpoint? Region { get; set; }

        /// <summary>
        /// Explicit credentials for the Bedrock client. Null (default) falls back to the default credential
        /// chain (environment, profile, EC2/ECS/Lambda role, etc.).
        /// </summary>
        public AWSCredentials? Credentials { get; set; }

        /// <summary>
        /// How a Bedrock or AWS error is handled. When true, the error denies the call (matching the
        /// toolkit's default-deny posture); when false, the error allows the call and is recorded in the
        /// decision metadata. Null (the default) means "unset": a standalone backend treats it as fail-closed,
        /// and the DI/MCP entry points fill it from the umbrella <c>FailClosed</c> so a per-feature value here
        /// overrides the umbrella default.
        /// </summary>
        public bool? FailClosed { get; set; }
    }
}

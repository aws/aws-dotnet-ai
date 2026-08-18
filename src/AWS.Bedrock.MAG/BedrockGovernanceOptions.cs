// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon;
using Amazon.Runtime;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Top-level configuration for the Bedrock governance backends, used by the MCP and DI entry points.
    /// Each feature can be toggled independently; the nested option objects carry the per-feature detail.
    /// </summary>
    public sealed class BedrockGovernanceOptions
    {
        /// <summary>Bedrock Guardrails policy backend configuration.</summary>
        public BedrockGuardrailsPolicyOptions Policy { get; } = new();

        /// <summary>When true (default), the Bedrock policy backend is added to the kernel's PolicyEngine.</summary>
        public bool EnablePolicy { get; set; } = true;

        /// <summary>
        /// When true, the toolkit's existing rule/OPA/Cedar policy backends are cleared before the Bedrock
        /// backend is added, so Bedrock is the sole policy evaluator (ML-only). Default false: additive.
        /// </summary>
        public bool ReplacePolicyBackends { get; set; }

        /// <summary>When true (default), MCP tool output is sanitized through Bedrock Guardrails.</summary>
        public bool EnablePiiSanitization { get; set; } = true;

        /// <summary>Bedrock Guardrails PII sanitization configuration.</summary>
        public BedrockSanitizationOptions Sanitization { get; } = new();

        /// <summary>When true (default), governance events are written to the CloudWatch audit sink.</summary>
        public bool EnableAudit { get; set; } = true;

        /// <summary>CloudWatch audit sink configuration.</summary>
        public CloudWatchAuditOptions Audit { get; } = new();

        /// <summary>Default region for all backends. Null uses the default chain. Per-feature Region overrides this.</summary>
        public RegionEndpoint? Region { get; set; }

        /// <summary>
        /// Default credentials for all backends. Null uses the default credential chain. A per-feature
        /// <c>Credentials</c> value overrides this.
        /// </summary>
        public AWSCredentials? Credentials { get; set; }

        /// <summary>When true (default), a Guardrails/AWS policy error denies the call. Flows into the policy backend.</summary>
        public bool FailClosed { get; set; } = true;
    }
}

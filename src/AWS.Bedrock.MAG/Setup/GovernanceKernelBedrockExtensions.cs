// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AgentGovernance;
using AWS.Bedrock.MAG.Audit;
using AWS.Bedrock.MAG.Policy;

namespace AWS.Bedrock.MAG
{
    /// <summary>
    /// Imperative entry points for code that already holds a <see cref="GovernanceKernel"/> and isn't using
    /// DI or the MCP builder (console apps, tests, custom hosts).
    /// </summary>
    public static class GovernanceKernelBedrockExtensions
    {
        /// <summary>Adds a Bedrock Guardrails policy backend to the kernel's PolicyEngine.</summary>
        public static GovernanceKernel AddBedrockGuardrailsPolicy(
            this GovernanceKernel kernel,
            Action<BedrockGuardrailsPolicyOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(kernel);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new BedrockGuardrailsPolicyOptions();
            configure(options);
            kernel.PolicyEngine.AddExternalBackend(new BedrockGuardrailsPolicyBackend(options));
            return kernel;
        }

        /// <summary>
        /// Subscribes a CloudWatch audit sink to the kernel's AuditEmitter. Returns the sink; dispose it to
        /// unsubscribe and flush buffered events.
        /// </summary>
        public static CloudWatchAuditSink AddCloudWatchAudit(
            this GovernanceKernel kernel,
            Action<CloudWatchAuditOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(kernel);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new CloudWatchAuditOptions();
            configure(options);
            var sink = new CloudWatchAuditSink(options);
            sink.Subscribe(kernel.AuditEmitter);
            return sink;
        }
    }
}

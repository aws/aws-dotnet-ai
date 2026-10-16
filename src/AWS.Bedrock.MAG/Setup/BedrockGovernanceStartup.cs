// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using AgentGovernance;
using AWS.Bedrock.MAG.Audit;
using AWS.Bedrock.MAG.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AWS.Bedrock.MAG.Setup
{
    /// <summary>
    /// Attaches the Bedrock backends to the DI-resolved <see cref="GovernanceKernel"/> at startup: adds the
    /// policy backend to the PolicyEngine and subscribes the CloudWatch sink to the AuditEmitter. Kept as a
    /// hosted service so the kernel is fully built before wiring.
    /// </summary>
    internal sealed class BedrockGovernanceStartup : IHostedService
    {
        private readonly BedrockGovernanceOptions _options;
        private readonly IServiceProvider _services;

        public BedrockGovernanceStartup(BedrockGovernanceOptions options, IServiceProvider services)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // PII-only usage needs no kernel; only require one when a kernel-bound feature is enabled.
            if (!_options.EnablePolicy && !_options.EnableAudit)
            {
                return Task.CompletedTask;
            }

            var kernel = _services.GetService<GovernanceKernel>()
                ?? throw new InvalidOperationException(
                    "Bedrock governance requires a GovernanceKernel in DI. Call the toolkit's " +
                    ".WithGovernance(...) on the MCP server (or register a GovernanceKernel) before adding " +
                    "Bedrock governance, or disable EnablePolicy and EnableAudit for PII-only sanitization.");

            if (_options.EnablePolicy)
            {
                if (_options.ReplacePolicyBackends)
                {
                    // ML-only: drop the toolkit's rule/OPA/Cedar backends (and loaded policies) so Bedrock
                    // is the sole evaluator.
                    kernel.PolicyEngine.ClearPolicies();
                }

                kernel.PolicyEngine.AddExternalBackend(new BedrockGuardrailsPolicyBackend(_options.Policy));
            }

            if (_options.EnableAudit)
            {
                var sink = _services.GetService<CloudWatchAuditSink>();
                sink?.Subscribe(kernel.AuditEmitter);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // The audit sink is a container-owned singleton; the ServiceProvider disposes it after all hosted
            // services have stopped. Disposing it here could shut auditing down while other services are still
            // stopping (and emitting events), and would double-dispose the same instance.
            return Task.CompletedTask;
        }
    }
}

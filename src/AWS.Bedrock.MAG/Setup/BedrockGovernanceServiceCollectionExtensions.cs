// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Audit;
using AWS.Bedrock.MAG.Setup;

// Placed in the DI namespace so the extension method is discoverable wherever IServiceCollection is in scope,
// without an extra using for AWS.Bedrock.MAG.
namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers the Bedrock policy backend and CloudWatch audit sink against a <c>GovernanceKernel</c>
    /// resolved from DI. Use this for non-MCP agents; MCP servers use <c>WithBedrockGovernance</c>.
    /// </summary>
    public static class BedrockGovernanceServiceCollectionExtensions
    {
        /// <summary>Adds the Bedrock policy backend and CloudWatch audit sink to the DI GovernanceKernel.</summary>
        public static IServiceCollection AddBedrockGovernance(
            this IServiceCollection services,
            Action<BedrockGovernanceOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            AddShared(services, configure);
            return services;
        }

        // Shared registration used by both the DI and MCP entry points. Returns the resolved options so the
        // MCP entry point can decide whether to add the PII decorator.
        internal static BedrockGovernanceOptions AddShared(IServiceCollection services, Action<BedrockGovernanceOptions> configure)
        {
            var options = new BedrockGovernanceOptions();
            configure(options);
            Normalize(options);
            Validate(options);

            services.AddSingleton(options);

            if (options.EnableAudit)
            {
                services.AddSingleton(_ => new CloudWatchAuditSink(options.Audit));
            }

            services.AddHostedService(sp => new BedrockGovernanceStartup(options, sp));
            return options;
        }

        // Top-level Region/FailClosed act as defaults; Sanitization reuses the policy guardrail when unset.
        internal static void Normalize(BedrockGovernanceOptions options)
        {
            options.Sanitization.GuardrailId ??= options.Policy.GuardrailId;
            options.Policy.FailClosed = options.FailClosed;

            if (options.Region is not null)
            {
                options.Policy.Region ??= options.Region;
                options.Sanitization.Region ??= options.Region;
                options.Audit.Region ??= options.Region;
            }

            if (options.Credentials is not null)
            {
                options.Policy.Credentials ??= options.Credentials;
                options.Sanitization.Credentials ??= options.Credentials;
                options.Audit.Credentials ??= options.Credentials;
            }
        }

        internal static void Validate(BedrockGovernanceOptions options)
        {
            if (options.EnablePolicy && string.IsNullOrWhiteSpace(options.Policy.GuardrailId))
            {
                throw new InvalidOperationException("EnablePolicy is true but Policy.GuardrailId is not set.");
            }

            if (options.EnablePiiSanitization && string.IsNullOrWhiteSpace(options.Sanitization.GuardrailId))
            {
                throw new InvalidOperationException(
                    "EnablePiiSanitization is true but no guardrail is set. Set Sanitization.GuardrailId or Policy.GuardrailId.");
            }
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using AgentGovernance;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Mcp;
using AWS.Bedrock.MAG.Policy;
using AWS.Bedrock.MAG.Setup;
using AWS.Bedrock.MAG.UnitTests.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Setup
{
    public class SetupWiringTests
    {
        private sealed class StubMcpServerBuilder : IMcpServerBuilder
        {
            public IServiceCollection Services { get; } = new ServiceCollection();
        }

        // --- Normalize ---

        [Fact]
        public void Normalize_falls_back_sanitization_guardrail_to_policy()
        {
            var options = new BedrockGovernanceOptions();
            options.Policy.GuardrailId = "gr-policy";

            BedrockGovernanceServiceCollectionExtensions.Normalize(options);

            Assert.Equal("gr-policy", options.Sanitization.GuardrailId);
        }

        [Fact]
        public void Normalize_does_not_override_explicit_sanitization_guardrail()
        {
            var options = new BedrockGovernanceOptions();
            options.Policy.GuardrailId = "gr-policy";
            options.Sanitization.GuardrailId = "gr-sanitize";

            BedrockGovernanceServiceCollectionExtensions.Normalize(options);

            Assert.Equal("gr-sanitize", options.Sanitization.GuardrailId);
        }

        [Fact]
        public void Normalize_propagates_region_to_features()
        {
            var options = new BedrockGovernanceOptions { Region = RegionEndpoint.USWest2 };
            options.Policy.GuardrailId = "gr";

            BedrockGovernanceServiceCollectionExtensions.Normalize(options);

            Assert.Equal(RegionEndpoint.USWest2, options.Policy.Region);
            Assert.Equal(RegionEndpoint.USWest2, options.Sanitization.Region);
            Assert.Equal(RegionEndpoint.USWest2, options.Audit.Region);
        }

        [Fact]
        public void Normalize_flows_failclosed_to_policy_when_policy_unset()
        {
            var options = new BedrockGovernanceOptions { FailClosed = false };
            options.Policy.GuardrailId = "gr";

            BedrockGovernanceServiceCollectionExtensions.Normalize(options);

            Assert.False(options.Policy.FailClosed);
        }

        [Fact]
        public void Normalize_keeps_explicit_policy_failclosed_over_umbrella()
        {
            // Umbrella wants fail-open, but the per-feature value is explicitly fail-closed: the explicit
            // per-feature value must win (regression guard for the ??= override).
            var options = new BedrockGovernanceOptions { FailClosed = false };
            options.Policy.GuardrailId = "gr";
            options.Policy.FailClosed = true;

            BedrockGovernanceServiceCollectionExtensions.Normalize(options);

            Assert.True(options.Policy.FailClosed);
        }

        // --- Validate (via the public entry point) ---

        [Fact]
        public void AddBedrockGovernance_throws_when_policy_guardrail_missing()
        {
            var services = new ServiceCollection();

            Assert.Throws<InvalidOperationException>(() =>
                services.AddBedrockGovernance(o =>
                {
                    o.EnablePolicy = true;
                    o.EnablePiiSanitization = false;
                    o.EnableAudit = false;
                }));
        }

        [Fact]
        public void AddBedrockGovernance_does_not_validate_sanitizer_for_audit_only_registration()
        {
            // PII sanitization is MCP-only; the DI path never uses it. An audit-only registration must not
            // fail for a missing sanitizer guardrail even though EnablePiiSanitization defaults to true.
            var services = new ServiceCollection();

            var ex = Record.Exception(() =>
                services.AddBedrockGovernance(o =>
                {
                    o.EnablePolicy = false;
                    o.EnableAudit = true;
                    // EnablePiiSanitization left at its default (true) with no guardrail configured.
                }));

            Assert.Null(ex);
        }

        [Fact]
        public void WithBedrockGovernance_throws_when_sanitization_guardrail_missing()
        {
            // The MCP path does register and run the sanitizer, so it must validate the guardrail.
            var builder = new StubMcpServerBuilder();

            Assert.Throws<InvalidOperationException>(() =>
                builder.WithBedrockGovernance(o =>
                {
                    o.EnablePolicy = false;
                    o.EnablePiiSanitization = true;
                    o.EnableAudit = false;
                }));
        }

        // --- Registration wiring (descriptor inspection, no provider build) ---

        [Fact]
        public void AddBedrockGovernance_registers_options_sink_and_hosted_service()
        {
            var services = new ServiceCollection();

            services.AddBedrockGovernance(o =>
            {
                o.Policy.GuardrailId = "gr";
                o.EnablePiiSanitization = false;
                o.EnableAudit = true;
            });

            Assert.Contains(services, d => d.ServiceType == typeof(BedrockGovernanceOptions));
            Assert.Contains(services, d => d.ServiceType == typeof(global::AWS.Bedrock.MAG.Audit.CloudWatchAuditSink));
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        }

        [Fact]
        public void AddBedrockGovernance_omits_sink_when_audit_disabled()
        {
            var services = new ServiceCollection();

            services.AddBedrockGovernance(o =>
            {
                o.Policy.GuardrailId = "gr";
                o.EnablePiiSanitization = false;
                o.EnableAudit = false;
            });

            Assert.DoesNotContain(services, d => d.ServiceType == typeof(global::AWS.Bedrock.MAG.Audit.CloudWatchAuditSink));
        }

        [Fact]
        public void WithBedrockGovernance_registers_sanitizer_and_postconfigure_when_pii_enabled()
        {
            var builder = new StubMcpServerBuilder();

            builder.WithBedrockGovernance(o =>
            {
                o.EnablePolicy = false;
                o.EnableAudit = false;
                o.EnablePiiSanitization = true;
                o.Sanitization.GuardrailId = "gr";
            });

            Assert.Contains(builder.Services, d => d.ServiceType == typeof(BedrockGuardrailsSanitizer));
            Assert.Contains(builder.Services, d => d.ServiceType == typeof(IPostConfigureOptions<McpServerOptions>));
        }

        [Fact]
        public void WithBedrockGovernance_skips_sanitizer_when_pii_disabled()
        {
            var builder = new StubMcpServerBuilder();

            builder.WithBedrockGovernance(o =>
            {
                o.Policy.GuardrailId = "gr";
                o.EnablePiiSanitization = false;
                o.EnableAudit = false;
            });

            Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(BedrockGuardrailsSanitizer));
        }

        // --- Imperative kernel extensions: argument guards (no AWS client construction) ---

        [Fact]
        public void AddBedrockGuardrailsPolicy_throws_on_null_kernel()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ((GovernanceKernel)null!).AddBedrockGuardrailsPolicy(_ => { }));
        }

        [Fact]
        public void AddCloudWatchAudit_throws_on_null_kernel()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ((GovernanceKernel)null!).AddCloudWatchAudit(_ => { }));
        }

        // --- Hosted service (BedrockGovernanceStartup) ---

        [Fact]
        public async Task Startup_throws_with_guidance_when_kernel_required_but_missing()
        {
            var options = new BedrockGovernanceOptions();
            options.Policy.GuardrailId = "gr";
            var startup = new BedrockGovernanceStartup(options, NullServiceProvider.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => startup.StartAsync(CancellationToken.None));
        }

        [Fact]
        public async Task Startup_is_noop_for_pii_only_without_kernel()
        {
            var options = new BedrockGovernanceOptions { EnablePolicy = false, EnableAudit = false, EnablePiiSanitization = true };
            options.Sanitization.GuardrailId = "gr";
            var startup = new BedrockGovernanceStartup(options, NullServiceProvider.Instance);

            // No kernel is registered; PII-only wiring must not require one.
            await startup.StartAsync(CancellationToken.None);
            await startup.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Startup_clears_existing_policy_backends_when_replace_is_set()
        {
            var kernel = new GovernanceKernel();
            kernel.PolicyEngine.LoadYaml(
                """
                apiVersion: governance.toolkit/v1
                name: baseline
                default_action: deny
                rules: []
                """);
            // Seed an external backend too, so the test verifies ReplacePolicyBackends clears external
            // backends (OPA/Cedar/custom), not just loaded YAML policies.
            kernel.PolicyEngine.AddExternalBackend(new StubExternalBackend("seeded-external"));
            Assert.Single(kernel.PolicyEngine.ListPolicies());
            Assert.Contains("seeded-external", kernel.PolicyEngine.ListExternalBackends());

            var services = new ServiceCollection();
            services.AddSingleton(kernel);
            using var provider = services.BuildServiceProvider();

            var options = new BedrockGovernanceOptions
            {
                EnablePolicy = true,
                EnableAudit = false,
                EnablePiiSanitization = false,
                ReplacePolicyBackends = true
            };
            options.Policy.GuardrailId = "gr";
            // Deterministic region so the Bedrock client constructs without relying on ambient AWS config,
            // letting StartAsync complete rather than throwing (which the old test silently swallowed).
            options.Policy.Region = RegionEndpoint.USWest2;

            var startup = new BedrockGovernanceStartup(options, provider);
            await startup.StartAsync(CancellationToken.None);

            // Both the loaded YAML policy and the seeded external backend are cleared; Bedrock is the sole
            // remaining external policy evaluator.
            Assert.Empty(kernel.PolicyEngine.ListPolicies());
            var backend = Assert.Single(kernel.PolicyEngine.ListExternalBackends());
            Assert.Equal(BedrockGuardrailsPolicyBackend.BackendName, backend);
        }

        // Minimal external backend for seeding the PolicyEngine; never evaluated in these wiring tests.
        private sealed class StubExternalBackend : global::AgentGovernance.Policy.IExternalPolicyBackend
        {
            public StubExternalBackend(string name) => Name = name;

            public string Name { get; }

            public global::AgentGovernance.Policy.ExternalPolicyDecision Evaluate(
                System.Collections.Generic.IReadOnlyDictionary<string, object> context)
                => new() { Backend = Name, Allowed = true, Reason = "stub" };
        }
    }
}

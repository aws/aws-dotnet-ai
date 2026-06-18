// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting.Internal;

namespace OpenTelemetry.Metrics;

/// <summary>
/// AgentCore OpenTelemetry instrumentation extensions for <see cref="MeterProviderBuilder"/>.
/// </summary>
public static class AgentCoreMeterProviderBuilderExtensions
{
    /// <summary>
    /// Subscribes the AgentCore meters to the <see cref="MeterProviderBuilder"/>.
    /// Meters include <c>AWS.AgentCore.Hosting</c>, <c>Experimental.Microsoft.Agents.AI</c>
    /// (Microsoft Agent Framework default), and <c>Experimental.Microsoft.Extensions.AI</c>
    /// (Microsoft.Extensions.AI default).
    /// </summary>
    /// <remarks>
    /// Use this when wiring up your own <see cref="MeterProviderBuilder"/> (e.g. inside an
    /// Aspire <c>ServiceDefaults</c> project or a custom OpenTelemetry pipeline). For a turnkey
    /// OTel pipeline targeting the AgentCore Runtime sidecar, set
    /// <c>AgentCoreOptions.EnableObservability</c> to <c>true</c> in <c>AddAgentCore()</c> instead.
    /// </remarks>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The same <see cref="MeterProviderBuilder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithMetrics(metrics => metrics
    ///         .AddAspNetCoreInstrumentation()
    ///         .AddHttpClientInstrumentation()
    ///         .AddAgentCoreInstrumentation()
    ///         .AddOtlpExporter());
    /// </code>
    /// </example>
    public static MeterProviderBuilder AddAgentCoreInstrumentation(this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AgentCoreObservability.EnrichMetrics(builder);
        return builder;
    }
}

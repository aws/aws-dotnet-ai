// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting.Internal;

namespace OpenTelemetry.Trace;

/// <summary>
/// AgentCore OpenTelemetry instrumentation extensions for <see cref="TracerProviderBuilder"/>.
/// </summary>
public static class AgentCoreTracerProviderBuilderExtensions
{
    /// <summary>
    /// Subscribes the AgentCore activity sources and AWS SDK instrumentation to the
    /// <see cref="TracerProviderBuilder"/>. Sources include <c>AWS.AgentCore.Hosting</c>,
    /// <c>Experimental.Microsoft.Agents.AI</c> (Microsoft Agent Framework default), and
    /// <c>Experimental.Microsoft.Extensions.AI</c> (Microsoft.Extensions.AI default).
    /// </summary>
    /// <remarks>
    /// Use this when wiring up your own <see cref="TracerProviderBuilder"/> (e.g. inside an
    /// Aspire <c>ServiceDefaults</c> project or a custom OpenTelemetry pipeline).
    /// </remarks>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The same <see cref="TracerProviderBuilder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing => tracing
    ///         .AddAspNetCoreInstrumentation()
    ///         .AddHttpClientInstrumentation()
    ///         .AddAgentCoreInstrumentation()
    ///         .AddOtlpExporter());
    /// </code>
    /// </example>
    public static TracerProviderBuilder AddAgentCoreInstrumentation(this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AgentCoreObservability.EnrichTracing(builder);
        return builder;
    }
}

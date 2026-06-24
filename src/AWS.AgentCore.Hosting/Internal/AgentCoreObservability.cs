// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// OpenTelemetry registration logic for AgentCore.
/// <para>
/// <see cref="EnrichTracing"/> and <see cref="EnrichMetrics"/> add AgentCore activity sources
/// and meters to a TracerProviderBuilder/MeterProviderBuilder. Used by the public
/// <c>AddAgentCoreInstrumentation()</c> extensions for users wiring their own OTel pipeline.
/// </para>
/// </summary>
internal static class AgentCoreObservability
{
    internal const string ActivitySourceName = "AWS.AgentCore.Hosting";
    internal const string MeterName = "AWS.AgentCore.Hosting";

    // Default ActivitySource and Meter names used by Microsoft Agent Framework's OpenTelemetryAgent
    // when no explicit sourceName is provided to .UseOpenTelemetry().
    internal const string MsAgentFrameworkSource = "Experimental.Microsoft.Agents.AI";

    // Default ActivitySource and Meter names used by Microsoft.Extensions.AI's OpenTelemetryChatClient
    // when no explicit sourceName is provided to .UseOpenTelemetry().
    internal const string MsExtensionsAiSource = "Experimental.Microsoft.Extensions.AI";

    /// <summary>
    /// Adds the AgentCore activity sources to the <see cref="TracerProviderBuilder"/>.
    /// </summary>
    internal static void EnrichTracing(TracerProviderBuilder tracing)
    {
        tracing
            .AddSource(ActivitySourceName)
            .AddSource(MsAgentFrameworkSource)
            .AddSource(MsExtensionsAiSource);
    }

    /// <summary>
    /// Adds the AgentCore meters to the <see cref="MeterProviderBuilder"/>.
    /// </summary>
    internal static void EnrichMetrics(MeterProviderBuilder metrics)
    {
        metrics
            .AddMeter(MeterName)
            .AddMeter(MsAgentFrameworkSource)
            .AddMeter(MsExtensionsAiSource);
    }
}

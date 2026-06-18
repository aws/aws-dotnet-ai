// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// OpenTelemetry GenAI semantic-convention metrics emitted for agent invocations
/// and memory operations.
/// See https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/gen-ai/gen-ai-metrics.md
/// </summary>
internal static class AgentCoreMetrics
{
    private static readonly Meter AgentMeter = new(AgentCoreObservability.MeterName);

    // Pre-computed operation-name tags to avoid per-call allocations on the hot path.
    private static readonly KeyValuePair<string, object?> OperationInvokeAgent = new("gen_ai.operation.name", "invoke_agent");
    private static readonly KeyValuePair<string, object?> OperationSearchMemory = new("gen_ai.operation.name", "search_memory");
    private static readonly KeyValuePair<string, object?> OperationUpsertMemory = new("gen_ai.operation.name", "upsert_memory");

    /// <summary>
    /// gen_ai.client.operation.duration — GenAI operation duration in seconds.
    /// Tagged with gen_ai.operation.name (required) and error.type (when the operation
    /// ended in error). The OpenTelemetry resource carries gen_ai.provider.name (set by
    /// AgentCoreObservability when a Bedrock ModelId is configured, or by the user via
    /// OTEL_RESOURCE_ATTRIBUTES).
    /// The histogram's count aggregation also serves as the invocation counter.
    /// </summary>
    private static readonly Histogram<double> OperationDuration =
        AgentMeter.CreateHistogram<double>(
            "gen_ai.client.operation.duration",
            unit: "s",
            description: "GenAI operation duration");

    /// <summary>
    /// Records the duration of an invoke_agent operation in seconds.
    /// </summary>
    /// <param name="seconds">Elapsed time in seconds.</param>
    /// <param name="errorType">
    /// Optional error.type when the operation failed. Skipped when null. Set to the
    /// exception's full type name or "_OTHER" when type is unknown.
    /// </param>
    public static void RecordInvocationDuration(double seconds, string? errorType)
    {
        var tags = new TagList { OperationInvokeAgent };
        if (errorType is not null)
            tags.Add("error.type", errorType);

        OperationDuration.Record(seconds, tags);
    }

    /// <summary>
    /// Records a search_memory operation. The duration value is currently unmeasured
    /// at the call sites; pass 0 if only the count is meaningful.
    /// </summary>
    public static void RecordMemoryLoad(double seconds = 0) =>
        OperationDuration.Record(seconds, OperationSearchMemory);

    /// <summary>
    /// Records an upsert_memory operation. The duration value is currently unmeasured
    /// at the call sites; pass 0 if only the count is meaningful.
    /// </summary>
    public static void RecordMemorySave(double seconds = 0) =>
        OperationDuration.Record(seconds, OperationUpsertMemory);
}

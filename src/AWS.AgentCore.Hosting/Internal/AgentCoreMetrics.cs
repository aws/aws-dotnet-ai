// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.Metrics;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// Custom metrics instruments for agent operations. Provides counters and histograms
/// for tracking invocations, invocation duration, and memory operations.
/// </summary>
internal static class AgentCoreMetrics
{
    private static readonly Meter AgentMeter = new(AgentCoreObservability.MeterName);

    // Pre-computed status tags to avoid per-call allocations on the hot path.
    private static readonly KeyValuePair<string, object?> StatusOkTag = new("status", "ok");
    private static readonly KeyValuePair<string, object?> StatusErrorTag = new("status", "error");

    // Pre-computed memory operation tags
    private static readonly KeyValuePair<string, object?> OperationLoadTag = new("operation.type", "load");
    private static readonly KeyValuePair<string, object?> OperationSaveTag = new("operation.type", "save");

    private static readonly Counter<long> InvocationCounter =
        AgentMeter.CreateCounter<long>(
            "aws.agentcore.hosting.invocations",
            unit: "{invocation}",
            description: "Number of agent invocations");

    private static readonly Histogram<double> InvocationDuration =
        AgentMeter.CreateHistogram<double>(
            "aws.agentcore.hosting.invocation.duration",
            unit: "s",
            description: "Duration of agent invocations in seconds");

    private static readonly Counter<long> MemoryOperationCounter =
        AgentMeter.CreateCounter<long>(
            "aws.agentcore.hosting.memory.operations",
            unit: "{operation}",
            description: "Number of AgentCore Memory operations");

    /// <summary>
    /// Records a single agent invocation, tagged with the outcome (ok or error).
    /// </summary>
    public static void RecordInvocation(bool isError = false) =>
        InvocationCounter.Add(1, isError ? StatusErrorTag : StatusOkTag);

    /// <summary>
    /// Records the duration of an agent invocation in seconds.
    /// </summary>
    public static void RecordInvocationDuration(double seconds) =>
        InvocationDuration.Record(seconds);

    /// <summary>
    /// Records a memory load operation.
    /// </summary>
    public static void RecordMemoryLoad() =>
        MemoryOperationCounter.Add(1, OperationLoadTag);

    /// <summary>
    /// Records a memory save operation.
    /// </summary>
    public static void RecordMemorySave() =>
        MemoryOperationCounter.Add(1, OperationSaveTag);
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Reflection;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// Custom activity source for agent-level spans. Creates activities for
/// AgentCore invocations with session and request metadata.
/// </summary>
internal static class AgentCoreActivitySource
{
    internal static readonly ActivitySource Source = new(
        AgentCoreObservability.ActivitySourceName,
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

    /// <summary>
    /// Starts an internal-kind activity representing an agent invocation,
    /// tagged with the session and request identifiers. Internal kind is used because
    /// the request's transport-level Server span is already created by the ASP.NET Core
    /// instrumentation; this activity captures the AgentCore-specific business operation
    /// nested under that Server span.
    /// </summary>
    public static Activity? StartInvocation(string? sessionId, string requestId)
    {
        var activity = Source.StartActivity("aws.agentcore.hosting.invocation", ActivityKind.Internal);
        if (sessionId is not null)
            activity?.SetTag("aws.agentcore.hosting.session_id", sessionId);
        activity?.SetTag("aws.agentcore.hosting.request_id", requestId);
        return activity;
    }
}

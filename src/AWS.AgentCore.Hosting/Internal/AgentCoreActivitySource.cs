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
    /// Starts an internal-kind activity representing an agent invocation, tagged with the
    /// OpenTelemetry GenAI semantic-convention <c>gen_ai.conversation.id</c> when a session
    /// is present. Internal kind is used because the request's transport-level Server span is
    /// already created by the ASP.NET Core instrumentation; this activity captures the
    /// AgentCore-specific business operation nested under that Server span.
    /// </summary>
    /// <remarks>
    /// Per the OTel GenAI conventions, individual request correlation uses the standard
    /// trace id / span id rather than a custom request id attribute, so we do not emit one.
    /// </remarks>
    public static Activity? StartInvocation(string? conversationId)
    {
        var activity = Source.StartActivity("aws.agentcore.hosting.invocation", ActivityKind.Internal);
        if (conversationId is not null)
            activity?.SetTag("gen_ai.conversation.id", conversationId);
        return activity;
    }
}

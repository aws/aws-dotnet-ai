// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Agents.AI;

namespace AWS.AgentCore.Internal;

/// <summary>
/// Creates <see cref="AgentSession"/> instances using the session ID from AgentCore HTTP headers.
/// </summary>
internal static class AgentCoreSessionFactory
{
    /// <summary>
    /// Creates an <see cref="AgentSession"/> with the session ID from the <see cref="AgentCoreRuntimeContext"/>.
    /// If no session ID is present in the context, creates a session without a conversation ID.
    /// Stores the runtime context in the session's state bag for access by context providers and middleware.
    /// </summary>
    public static async Task<AgentSession> CreateSessionAsync(
        ChatClientAgent agent,
        AgentCoreRuntimeContext? runtimeContext,
        CancellationToken cancellationToken = default)
    {
        var sessionId = runtimeContext?.SessionId;

        var session = string.IsNullOrEmpty(sessionId)
            ? await agent.CreateSessionAsync(cancellationToken: cancellationToken)
            : await agent.CreateSessionAsync(sessionId, cancellationToken);

        // Store runtime context in session state for access by context providers/middleware
        if (runtimeContext is not null)
        {
            session.StateBag.SetValue(AgentCoreRuntimeContextProvider.ContextKey, runtimeContext);
        }

        return session;
    }
}

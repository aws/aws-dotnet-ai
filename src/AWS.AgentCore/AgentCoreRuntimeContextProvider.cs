// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Agents.AI;

namespace AWS.AgentCore;

/// <summary>
/// An <see cref="AIContextProvider"/> that makes <see cref="AgentCoreRuntimeContext"/> data
/// available within the Microsoft Agent Framework pipeline.
/// <para>
/// Registered automatically by <see cref="Extensions.AgentCoreBuilderExtensions.AddAgentCore"/>.
/// Downstream middleware and context providers can access the runtime context (session ID,
/// request ID, access tokens, custom headers) via the session's state bag using <see cref="ContextKey"/>,
/// or via the ambient <see cref="Current"/> property which is set automatically by the endpoint handlers.
/// </para>
/// </summary>
public class AgentCoreRuntimeContextProvider : AIContextProvider
{
    /// <summary>
    /// Key used to store/retrieve <see cref="AgentCoreRuntimeContext"/> in the agent session state.
    /// </summary>
    public const string ContextKey = "AgentCore.RuntimeContext";

    private static readonly AsyncLocal<AgentCoreRuntimeContext?> _currentContext = new();

    /// <summary>
    /// Gets or sets the <see cref="AgentCoreRuntimeContext"/> for the current async execution context.
    /// This is set automatically by the AgentCore endpoint handlers (<c>MapAgentCore</c>) and
    /// flows through async calls, making it available to the Memory provider without requiring
    /// manual session StateBag population.
    /// </summary>
    public static AgentCoreRuntimeContext? CurrentContext
    {
        get => _currentContext.Value;
        set => _currentContext.Value = value;
    }
}

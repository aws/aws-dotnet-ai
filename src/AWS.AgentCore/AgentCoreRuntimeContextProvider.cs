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
/// request ID, access tokens, custom headers) via the session's state bag using <see cref="ContextKey"/>.
/// </para>
/// </summary>
public class AgentCoreRuntimeContextProvider : AIContextProvider
{
    /// <summary>
    /// Key used to store/retrieve <see cref="AgentCoreRuntimeContext"/> in the agent session state.
    /// </summary>
    public const string ContextKey = "AgentCore.RuntimeContext";
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AspireAppHost;

/// <summary>
/// Configuration options for <see cref="AgentCoreTestingExtensions.AddAgentCoreRuntime{TProject}"/>.
/// </summary>
public sealed class AgentCoreTestingOptions
{
    /// <summary>
    /// When <c>true</c>, emulator logs (Runtime Emulator, Chat App, Memory Emulator)
    /// are forwarded to the agent's resource log stream in the Aspire Dashboard.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool IncludeEmulatorLogs { get; set; }
}

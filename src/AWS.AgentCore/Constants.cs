// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Constants used throughout the AWS.AgentCore library.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// Environment variable name for the AgentCore Memory ID.
    /// When set, the Memory provider uses this value as the MemoryId for all Memory operations
    /// (unless overridden by <see cref="AgentCoreOptions.MemoryId"/>).
    /// </summary>
    internal const string MemoryIdEnvironmentVariable = "AWS_AGENTCORE_MEMORY_ID";
}

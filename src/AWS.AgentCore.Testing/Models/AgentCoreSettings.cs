// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Testing.Models;

/// <summary>
/// Configuration settings for the embedded ChatBot UI's connection to the AgentCore Runtime Emulator.
/// </summary>
public class AgentCoreSettings
{
    /// <summary>
    /// The AgentCore Runtime ARN used in SDK requests. When running locally via the
    /// runtime emulator, this can be any non-empty string (e.g., "local-agent").
    /// </summary>
    public string RuntimeArn { get; set; } = string.Empty;

    /// <summary>
    /// Whether the ChatBot UI should use SSE streaming mode for agent responses.
    /// When false, standard JSON request/response mode is used.
    /// </summary>
    public bool UseStreaming { get; set; }
}

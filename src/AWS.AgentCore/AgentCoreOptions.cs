// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Options for configuring the AgentCore services.
/// </summary>
public class AgentCoreOptions
{
    /// <summary>
    /// The Bedrock model ID to use for the chat client.
    /// </summary>
    public string ModelId { get; set; } = "anthropic.claude-sonnet-4-20250514-v1:0";

    /// <summary>
    /// The port to listen on. AgentCore Runtime expects 8080. Default: 8080.
    /// </summary>
    public int Port { get; set; } = 8080;
}

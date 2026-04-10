// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Options for configuring the AgentCore services.
/// </summary>
public class AgentCoreOptions
{
    /// <summary>
    /// The Bedrock model ID to use for the chat client. This field is required and must be set
    /// via the <c>AddAgentCore</c> configure callback.
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>
    /// The port to listen on. AgentCore Runtime expects 8080. Default: 8080.
    /// </summary>
    public int Port { get; set; } = 8080;
}

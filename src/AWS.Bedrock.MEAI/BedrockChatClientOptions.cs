// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.AI;

namespace AWS.Bedrock.MEAI;

/// <summary>
/// Optional settings that control how a <see cref="BedrockChatClient"/> maps Microsoft.Extensions.AI
/// requests and responses to the Amazon Bedrock Converse API.
/// </summary>
/// <remarks>
/// This type exists so new opt-in behaviors can be added as properties without introducing another
/// <see cref="BedrockChatClient"/> constructor or <c>AsIChatClient</c> overload. All properties default
/// to values that preserve the behavior of a client created without options.
/// </remarks>
public sealed class BedrockChatClientOptions
{
    /// <summary>
    /// Gets or sets how <see cref="ChatOptions.ResponseFormat"/> (structured output) is realized against
    /// the Converse API. Defaults to <see cref="BedrockStructuredOutputMode.SyntheticTool"/>.
    /// </summary>
    public BedrockStructuredOutputMode StructuredOutputMode { get; set; } = BedrockStructuredOutputMode.SyntheticTool;

    /// <summary>
    /// Gets or sets whether consecutive request messages that map to the same Converse role are combined
    /// into a single message. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Both <see cref="ChatRole.Tool"/> and <see cref="ChatRole.User"/> map to the Converse <c>user</c>
    /// role, so a history such as <c>user → assistant(toolCall) → tool(result) → user(text)</c> produces
    /// two consecutive <c>user</c> messages. When this is <see langword="true"/> they are combined into a
    /// single message, preserving content-block order, which gives replayed histories a normalized request
    /// shape. Messages separated by a cache point are never combined (that would move a prompt-cache
    /// boundary), and caller-supplied messages (from <see cref="ChatOptions.RawRepresentationFactory"/>)
    /// are not modified.
    /// </remarks>
    public bool CoalesceConsecutiveMessages { get; set; }
}

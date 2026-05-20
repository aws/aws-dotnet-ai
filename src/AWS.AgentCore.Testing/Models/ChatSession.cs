// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Testing.Models;

/// <summary>
/// Represents a chat conversation session containing an ordered list of messages.
/// </summary>
public class ChatSession
{
    /// <summary>
    /// Unique identifier for this session. Also used as the RuntimeSessionId when invoking the agent.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display title for this session. Auto-generated from the first user message (truncated to 40 characters).
    /// </summary>
    public string Title { get; set; } = "New Chat";

    /// <summary>
    /// The ordered list of messages in this conversation.
    /// </summary>
    public List<ChatMessage> Messages { get; set; } = [];

    /// <summary>
    /// UTC timestamp when this session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the most recent message in this session.
    /// </summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

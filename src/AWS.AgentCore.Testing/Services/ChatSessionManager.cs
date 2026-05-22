// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using AWS.AgentCore.Testing.Models;

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// In-memory chat session manager. Tracks conversations across the application lifetime.
/// Registered as a singleton — all requests share the same instance.
/// Uses ConcurrentDictionary for thread safety against concurrent async operations.
/// </summary>
public class ChatSessionManager
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();

    /// <summary>
    /// All sessions ordered by most recent activity.
    /// </summary>
    public IReadOnlyList<ChatSession> Sessions =>
        _sessions.Values.OrderByDescending(s => s.LastMessageAt).ToList();

    /// <summary>
    /// Returns a session by ID, or null if not found.
    /// </summary>
    /// <param name="id">The session ID to look up.</param>
    public ChatSession? GetSession(string id)
    {
        return _sessions.TryGetValue(id, out var session) ? session : null;
    }

    /// <summary>
    /// Returns an existing session by ID, or creates a new one if the ID is null or not found.
    /// </summary>
    /// <param name="id">The session ID to look up, or null to create a new session.</param>
    public ChatSession GetOrCreateSession(string? id)
    {
        if (!string.IsNullOrEmpty(id) && _sessions.TryGetValue(id, out var existing))
        {
            return existing;
        }
        return CreateSession();
    }

    /// <summary>
    /// Creates a new empty session.
    /// </summary>
    public ChatSession CreateSession()
    {
        var session = new ChatSession();
        _sessions[session.Id] = session;
        return session;
    }

    /// <summary>
    /// Deletes a session by ID.
    /// </summary>
    /// <param name="sessionId">The session to delete.</param>
    public void DeleteSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Appends a message to a session. Auto-titles the session from the first user message.
    /// </summary>
    /// <param name="sessionId">The session to add the message to.</param>
    /// <param name="message">The message to append.</param>
    public void AddMessage(string sessionId, ChatMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Add(message);
            session.LastMessageAt = DateTime.UtcNow;

            if (session.Title == "New Chat" && message.Role == ChatRole.User)
            {
                session.Title = message.Content.Length > 40
                    ? message.Content[..40] + "..."
                    : message.Content;
            }
        }
    }

}

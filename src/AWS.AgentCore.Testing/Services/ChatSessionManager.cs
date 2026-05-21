// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using AWS.AgentCore.Testing.Models;

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// In-memory chat session manager. Tracks conversations across the user's browser session.
/// Scoped per Blazor circuit — each browser tab gets its own instance.
/// Uses ConcurrentDictionary for thread safety against concurrent async operations.
/// </summary>
public class ChatSessionManager
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new();
    private string? _activeSessionId;

    /// <summary>
    /// Fired whenever sessions or messages change, triggering UI re-render.
    /// </summary>
    public event Action? OnChange;

    /// <summary>
    /// All sessions ordered by most recent activity.
    /// </summary>
    public IReadOnlyList<ChatSession> Sessions =>
        _sessions.Values.OrderByDescending(s => s.LastMessageAt).ToList();

    /// <summary>
    /// The currently selected session, or null if none is active.
    /// </summary>
    public ChatSession? ActiveSession =>
        _activeSessionId != null && _sessions.TryGetValue(_activeSessionId, out var session) ? session : null;

    /// <summary>
    /// Creates a new empty session and sets it as active.
    /// </summary>
    public ChatSession CreateSession()
    {
        var session = new ChatSession();
        _sessions[session.Id] = session;
        _activeSessionId = session.Id;
        OnChange?.Invoke();
        return session;
    }

    /// <summary>
    /// Switches the active session to the specified session ID.
    /// </summary>
    /// <param name="sessionId">The session to activate.</param>
    public void SetActiveSession(string sessionId)
    {
        if (_sessions.ContainsKey(sessionId))
        {
            _activeSessionId = sessionId;
            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Deletes a session and selects the next available session as active.
    /// </summary>
    /// <param name="sessionId">The session to delete.</param>
    public void DeleteSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        if (_activeSessionId == sessionId)
        {
            _activeSessionId = _sessions.Keys.FirstOrDefault();
        }
        OnChange?.Invoke();
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

            OnChange?.Invoke();
        }
    }

    /// <summary>
    /// Updates the content of an existing message (e.g., for streaming or error updates).
    /// </summary>
    /// <param name="sessionId">The session containing the message.</param>
    /// <param name="messageId">The ID of the message to update.</param>
    /// <param name="content">The new content.</param>
    public void UpdateMessage(string sessionId, string messageId, string content)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            var msg = session.Messages.FirstOrDefault(m => m.Id == messageId);
            if (msg != null)
            {
                msg.Content = content;
                OnChange?.Invoke();
            }
        }
    }

    /// <summary>
    /// Returns the active session, creating a new one if none exists.
    /// </summary>
    public ChatSession GetOrCreateActiveSession()
    {
        return ActiveSession ?? CreateSession();
    }
}

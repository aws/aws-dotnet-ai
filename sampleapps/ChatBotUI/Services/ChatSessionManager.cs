// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using ChatBotUI.Models;

namespace ChatBotUI.Services;

/// <summary>
/// In-memory chat session manager. Tracks conversations across the user's browser session.
/// </summary>
public class ChatSessionManager
{
    private readonly Dictionary<string, ChatSession> _sessions = new();
    private string? _activeSessionId;

    public event Action? OnChange;

    public IReadOnlyList<ChatSession> Sessions =>
        _sessions.Values.OrderByDescending(s => s.LastMessageAt).ToList();

    public ChatSession? ActiveSession =>
        _activeSessionId != null && _sessions.TryGetValue(_activeSessionId, out var session) ? session : null;

    public ChatSession CreateSession()
    {
        var session = new ChatSession();
        _sessions[session.Id] = session;
        _activeSessionId = session.Id;
        OnChange?.Invoke();
        return session;
    }

    public void SetActiveSession(string sessionId)
    {
        if (_sessions.ContainsKey(sessionId))
        {
            _activeSessionId = sessionId;
            OnChange?.Invoke();
        }
    }

    public void DeleteSession(string sessionId)
    {
        _sessions.Remove(sessionId);
        if (_activeSessionId == sessionId)
        {
            _activeSessionId = _sessions.Keys.FirstOrDefault();
        }
        OnChange?.Invoke();
    }

    public void AddMessage(string sessionId, ChatMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Add(message);
            session.LastMessageAt = DateTime.UtcNow;

            // Auto-title from first user message
            if (session.Title == "New Chat" && message.Role == ChatRole.User)
            {
                session.Title = message.Content.Length > 40
                    ? message.Content[..40] + "..."
                    : message.Content;
            }

            OnChange?.Invoke();
        }
    }

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

    public ChatSession GetOrCreateActiveSession()
    {
        return ActiveSession ?? CreateSession();
    }
}

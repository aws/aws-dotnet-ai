using System.Collections.Concurrent;
using System.Text;
using AWS.AgentCore.Testing.Emulators.Memory.Models;

namespace AWS.AgentCore.Testing.Emulators.Memory;

/// <summary>
/// In-memory implementation of the event store for the Memory Emulator.
/// Stores conversation events keyed by {memoryId}::{actorId}::{sessionId}.
/// Thread-safe via ConcurrentDictionary with lock on list append.
/// </summary>
public class InMemoryEventStore
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _events = new();
    private const int DefaultPageSize = 50;

    /// <summary>
    /// Stores a new event and returns the created event response.
    /// </summary>
    public CreateEventApiResponse CreateEvent(string memoryId, CreateEventApiRequest request)
    {
        var key = BuildKey(memoryId, request.ActorId, request.SessionId);
        var storedEvent = new StoredEvent
        {
            EventId = Guid.NewGuid().ToString(),
            MemoryId = memoryId,
            ActorId = request.ActorId,
            SessionId = request.SessionId,
            EventTimestamp = request.EventTimestamp.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(request.EventTimestamp.Value).UtcDateTime
                : DateTime.UtcNow,
            Payload = request.Payload
        };

        _events.AddOrUpdate(key,
            _ => new List<StoredEvent> { storedEvent },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(storedEvent);
                }
                return list;
            });

        return new CreateEventApiResponse { Event = ToApiEvent(storedEvent, includePayloads: true) };
    }

    /// <summary>
    /// Lists events filtered by memoryId/actorId/sessionId with pagination and optional payload inclusion.
    /// Returns events newest-first (most recent <c>EventTimestamp</c> first), matching the
    /// ordering of the real Amazon Bedrock AgentCore Memory <c>ListEvents</c> API. Consumers
    /// that need chronological order must sort ascending themselves.
    /// </summary>
    /// <exception cref="InvalidNextTokenException">Thrown when the nextToken is malformed or not a valid pagination token.</exception>
    public ListEventsApiResponse ListEvents(
        string memoryId, string actorId, string sessionId,
        bool? includePayloads, int? maxResults, string? nextToken)
    {
        var key = BuildKey(memoryId, actorId, sessionId);
        if (!_events.TryGetValue(key, out var events))
            return new ListEventsApiResponse { Events = [], NextToken = null };

        var pageSize = maxResults ?? DefaultPageSize;

        if (!TryDecodeNextToken(nextToken, out var startIndex))
            throw new InvalidNextTokenException(nextToken);

        List<StoredEvent> snapshot;
        lock (events)
        {
            // Newest-first, matching the real AgentCore Memory ListEvents API.
            snapshot = events.OrderByDescending(e => e.EventTimestamp).ToList();
        }

        var page = snapshot
            .Skip(startIndex)
            .Take(pageSize + 1) // Take one extra to determine if there's a next page
            .ToList();

        var hasMore = page.Count > pageSize;
        var resultEvents = page.Take(pageSize).ToList();

        return new ListEventsApiResponse
        {
            Events = resultEvents.Select(e => ToApiEvent(e, includePayloads ?? false)).ToList(),
            NextToken = hasMore ? EncodeNextToken(startIndex + pageSize) : null
        };
    }

    private static string BuildKey(string memoryId, string actorId, string sessionId)
        => $"{memoryId}::{actorId}::{sessionId}";

    private static EventModel ToApiEvent(StoredEvent storedEvent, bool includePayloads)
    {
        return new EventModel
        {
            EventId = storedEvent.EventId,
            MemoryId = storedEvent.MemoryId,
            ActorId = storedEvent.ActorId,
            SessionId = storedEvent.SessionId,
            EventTimestamp = new DateTimeOffset(storedEvent.EventTimestamp, TimeSpan.Zero).ToUnixTimeSeconds(),
            Payload = includePayloads ? storedEvent.Payload : null
        };
    }

    private static string EncodeNextToken(int offset)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));

    private static bool TryDecodeNextToken(string? nextToken, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(nextToken))
            return true;

        try
        {
            var bytes = Convert.FromBase64String(nextToken);
            var text = Encoding.UTF8.GetString(bytes);
            return int.TryParse(text, out offset) && offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Thrown when a pagination NextToken cannot be decoded.
/// The HTTP layer should translate this into a 400 Bad Request.
/// </summary>
public class InvalidNextTokenException(string? token)
    : Exception($"Invalid NextToken: '{token}' is not a valid pagination token.")
{
    /// <summary>The invalid token value that was provided.</summary>
    public string? Token { get; } = token;
}

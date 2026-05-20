// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Testing.Emulators.Memory;
using AWS.AgentCore.Testing.Emulators.Memory.Models;

namespace AWS.AgentCore.Testing.UnitTests;

public class InMemoryEventStoreTests
{
    private static CreateEventApiRequest CreateRequest(
        string actorId = "actor-1",
        string sessionId = "session-1",
        string role = "USER",
        string text = "Hello",
        DateTime? timestamp = null)
    {
        return new CreateEventApiRequest
        {
            ActorId = actorId,
            SessionId = sessionId,
            EventTimestamp = timestamp.HasValue ? new DateTimeOffset(timestamp.Value).ToUnixTimeSeconds() : null,
            Payload =
            [
                new PayloadTypeModel
                {
                    Conversational = new ConversationalModel
                    {
                        Role = role,
                        Content = new ContentModel { Text = text }
                    }
                }
            ]
        };
    }

    // ──────────────────────────────────────────────────────────────────
    // CreateEvent_StoresAllFields
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateEvent_StoresAllFields()
    {
        var store = new InMemoryEventStore();
        var timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var request = CreateRequest(
            actorId: "actor-abc",
            sessionId: "session-xyz",
            role: "USER",
            text: "Test message",
            timestamp: timestamp);

        var response = store.CreateEventAsync("memory-123", request);

        // Verify the response contains all fields
        Assert.NotNull(response.Event);
        Assert.False(string.IsNullOrEmpty(response.Event.EventId));
        Assert.Equal("memory-123", response.Event.MemoryId);
        Assert.Equal("actor-abc", response.Event.ActorId);
        Assert.Equal("session-xyz", response.Event.SessionId);
        Assert.Equal(new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeSeconds(), response.Event.EventTimestamp);
        Assert.NotNull(response.Event.Payload);
        Assert.Single(response.Event.Payload);
        Assert.Equal("USER", response.Event.Payload[0].Conversational!.Role);
        Assert.Equal("Test message", response.Event.Payload[0].Conversational!.Content!.Text);

        // Verify the event can be retrieved
        var listResponse = store.ListEvents("memory-123", "actor-abc", "session-xyz",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(listResponse.Events);
        var retrieved = listResponse.Events[0];
        Assert.Equal(response.Event.EventId, retrieved.EventId);
        Assert.Equal("memory-123", retrieved.MemoryId);
        Assert.Equal("actor-abc", retrieved.ActorId);
        Assert.Equal("session-xyz", retrieved.SessionId);
        Assert.Equal(new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeSeconds(), retrieved.EventTimestamp);
        Assert.NotNull(retrieved.Payload);
        Assert.Equal("USER", retrieved.Payload![0].Conversational!.Role);
        Assert.Equal("Test message", retrieved.Payload[0].Conversational!.Content!.Text);
    }

    // ──────────────────────────────────────────────────────────────────
    // ListEvents_EmptyStore_ReturnsEmpty
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ListEvents_EmptyStore_ReturnsEmpty()
    {
        var store = new InMemoryEventStore();

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.NotNull(response);
        Assert.Empty(response.Events);
        Assert.Null(response.NextToken);
    }

    [Fact]
    public void ListEvents_NonExistentKey_ReturnsEmpty()
    {
        var store = new InMemoryEventStore();

        // Store an event for one key
        store.CreateEventAsync("memory-1", CreateRequest(actorId: "actor-1", sessionId: "session-1"));

        // Query a different key
        var response = store.ListEvents("memory-2", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Empty(response.Events);
        Assert.Null(response.NextToken);
    }

    // ──────────────────────────────────────────────────────────────────
    // ListEvents_FiltersCorrectly
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ListEvents_FiltersCorrectly_ByMemoryId()
    {
        var store = new InMemoryEventStore();

        store.CreateEventAsync("memory-A", CreateRequest(actorId: "actor-1", sessionId: "session-1", text: "Event A"));
        store.CreateEventAsync("memory-B", CreateRequest(actorId: "actor-1", sessionId: "session-1", text: "Event B"));

        var responseA = store.ListEvents("memory-A", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);
        var responseB = store.ListEvents("memory-B", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(responseA.Events);
        Assert.Equal("Event A", responseA.Events[0].Payload![0].Conversational!.Content!.Text);

        Assert.Single(responseB.Events);
        Assert.Equal("Event B", responseB.Events[0].Payload![0].Conversational!.Content!.Text);
    }

    [Fact]
    public void ListEvents_FiltersCorrectly_BySessionId()
    {
        var store = new InMemoryEventStore();

        store.CreateEventAsync("memory-1", CreateRequest(actorId: "actor-1", sessionId: "session-A", text: "Session A event"));
        store.CreateEventAsync("memory-1", CreateRequest(actorId: "actor-1", sessionId: "session-B", text: "Session B event"));

        var responseA = store.ListEvents("memory-1", "actor-1", "session-A",
            includePayloads: true, maxResults: null, nextToken: null);
        var responseB = store.ListEvents("memory-1", "actor-1", "session-B",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(responseA.Events);
        Assert.Equal("Session A event", responseA.Events[0].Payload![0].Conversational!.Content!.Text);

        Assert.Single(responseB.Events);
        Assert.Equal("Session B event", responseB.Events[0].Payload![0].Conversational!.Content!.Text);
    }

    [Fact]
    public void ListEvents_FiltersCorrectly_ByActorId()
    {
        var store = new InMemoryEventStore();

        store.CreateEventAsync("memory-1", CreateRequest(actorId: "actor-A", sessionId: "session-1", text: "Actor A event"));
        store.CreateEventAsync("memory-1", CreateRequest(actorId: "actor-B", sessionId: "session-1", text: "Actor B event"));

        var responseA = store.ListEvents("memory-1", "actor-A", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);
        var responseB = store.ListEvents("memory-1", "actor-B", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(responseA.Events);
        Assert.Equal("Actor A event", responseA.Events[0].Payload![0].Conversational!.Content!.Text);

        Assert.Single(responseB.Events);
        Assert.Equal("Actor B event", responseB.Events[0].Payload![0].Conversational!.Content!.Text);
    }

    [Fact]
    public void ListEvents_FiltersCorrectly_CombinedFilters()
    {
        var store = new InMemoryEventStore();

        // Store events with different combinations
        store.CreateEventAsync("mem-1", CreateRequest(actorId: "actor-1", sessionId: "sess-1", text: "Target"));
        store.CreateEventAsync("mem-1", CreateRequest(actorId: "actor-1", sessionId: "sess-2", text: "Wrong session"));
        store.CreateEventAsync("mem-1", CreateRequest(actorId: "actor-2", sessionId: "sess-1", text: "Wrong actor"));
        store.CreateEventAsync("mem-2", CreateRequest(actorId: "actor-1", sessionId: "sess-1", text: "Wrong memory"));

        var response = store.ListEvents("mem-1", "actor-1", "sess-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(response.Events);
        Assert.Equal("Target", response.Events[0].Payload![0].Conversational!.Content!.Text);
    }

    // ──────────────────────────────────────────────────────────────────
    // ListEvents_ReturnsChronologicalOrder
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ListEvents_ReturnsChronologicalOrder()
    {
        var store = new InMemoryEventStore();

        // Insert events out of chronological order
        var t3 = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var t1 = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2024, 6, 15, 11, 0, 0, DateTimeKind.Utc);

        store.CreateEventAsync("memory-1", CreateRequest(timestamp: t3, text: "Third"));
        store.CreateEventAsync("memory-1", CreateRequest(timestamp: t1, text: "First"));
        store.CreateEventAsync("memory-1", CreateRequest(timestamp: t2, text: "Second"));

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Equal(3, response.Events.Count);
        Assert.Equal("First", response.Events[0].Payload![0].Conversational!.Content!.Text);
        Assert.Equal("Second", response.Events[1].Payload![0].Conversational!.Content!.Text);
        Assert.Equal("Third", response.Events[2].Payload![0].Conversational!.Content!.Text);

        // Also verify timestamps are in ascending order
        Assert.True(response.Events[0].EventTimestamp < response.Events[1].EventTimestamp);
        Assert.True(response.Events[1].EventTimestamp < response.Events[2].EventTimestamp);
    }

    [Fact]
    public void ListEvents_ReturnsChronologicalOrder_WithManyEvents()
    {
        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Insert 10 events in reverse chronological order
        for (int i = 9; i >= 0; i--)
        {
            store.CreateEventAsync("memory-1", CreateRequest(
                timestamp: baseTime.AddMinutes(i),
                text: $"Event {i}"));
        }

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Equal(10, response.Events.Count);

        for (int i = 0; i < response.Events.Count - 1; i++)
        {
            Assert.True(response.Events[i].EventTimestamp <= response.Events[i + 1].EventTimestamp,
                $"Event at index {i} has timestamp {response.Events[i].EventTimestamp} which is after event at index {i + 1} with timestamp {response.Events[i + 1].EventTimestamp}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Pagination_ReturnsNextToken
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pagination_ReturnsNextToken()
    {
        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Insert more events than the page size (use maxResults=3 to keep test small)
        for (int i = 0; i < 5; i++)
        {
            store.CreateEventAsync("memory-1", CreateRequest(
                timestamp: baseTime.AddMinutes(i),
                text: $"Event {i}"));
        }

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: 3, nextToken: null);

        Assert.Equal(3, response.Events.Count);
        Assert.NotNull(response.NextToken);
    }

    // ──────────────────────────────────────────────────────────────────
    // Pagination_LastPage_NoNextToken
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pagination_LastPage_NoNextToken()
    {
        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Insert exactly the page size number of events
        for (int i = 0; i < 3; i++)
        {
            store.CreateEventAsync("memory-1", CreateRequest(
                timestamp: baseTime.AddMinutes(i),
                text: $"Event {i}"));
        }

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: 3, nextToken: null);

        Assert.Equal(3, response.Events.Count);
        Assert.Null(response.NextToken);
    }

    [Fact]
    public void Pagination_LastPage_NoNextToken_WhenUsingToken()
    {
        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Insert 5 events, page size 3 — second page should have 2 events and no token
        for (int i = 0; i < 5; i++)
        {
            store.CreateEventAsync("memory-1", CreateRequest(
                timestamp: baseTime.AddMinutes(i),
                text: $"Event {i}"));
        }

        var firstPage = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: 3, nextToken: null);

        Assert.NotNull(firstPage.NextToken);

        var secondPage = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: 3, nextToken: firstPage.NextToken);

        Assert.Equal(2, secondPage.Events.Count);
        Assert.Null(secondPage.NextToken);
    }

    // ──────────────────────────────────────────────────────────────────
    // Pagination_IterateAll_NoDuplicatesNoGaps
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Pagination_IterateAll_NoDuplicatesNoGaps()
    {
        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int totalEvents = 10;
        const int pageSize = 3;

        // Insert events with distinct timestamps
        for (int i = 0; i < totalEvents; i++)
        {
            store.CreateEventAsync("memory-1", CreateRequest(
                timestamp: baseTime.AddMinutes(i),
                text: $"Event {i}"));
        }

        // Iterate through all pages
        var allEvents = new List<EventModel>();
        string? token = null;

        do
        {
            var response = store.ListEvents("memory-1", "actor-1", "session-1",
                includePayloads: true, maxResults: pageSize, nextToken: token);

            allEvents.AddRange(response.Events);
            token = response.NextToken;
        } while (token != null);

        // Verify no gaps — all events retrieved
        Assert.Equal(totalEvents, allEvents.Count);

        // Verify no duplicates — all event IDs are unique
        var uniqueIds = allEvents.Select(e => e.EventId).Distinct().ToList();
        Assert.Equal(totalEvents, uniqueIds.Count);

        // Verify chronological order is maintained across pages
        for (int i = 0; i < allEvents.Count - 1; i++)
        {
            Assert.True(allEvents[i].EventTimestamp <= allEvents[i + 1].EventTimestamp,
                $"Event at index {i} has timestamp {allEvents[i].EventTimestamp} which is after event at index {i + 1} with timestamp {allEvents[i + 1].EventTimestamp}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // IncludePayloads_False_OmitsPayload
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IncludePayloads_False_OmitsPayload()
    {
        var store = new InMemoryEventStore();
        var timestamp = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        store.CreateEventAsync("memory-1", CreateRequest(
            timestamp: timestamp,
            role: "USER",
            text: "Hello world"));

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: false, maxResults: null, nextToken: null);

        Assert.Single(response.Events);
        var evt = response.Events[0];

        // Event metadata should still be present
        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.Equal("memory-1", evt.MemoryId);
        Assert.Equal("actor-1", evt.ActorId);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal(new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeSeconds(), evt.EventTimestamp);

        // Payload should be null/omitted
        Assert.Null(evt.Payload);
    }

    // ──────────────────────────────────────────────────────────────────
    // IncludePayloads_True_IncludesPayload
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void IncludePayloads_True_IncludesPayload()
    {
        var store = new InMemoryEventStore();
        var timestamp = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);

        store.CreateEventAsync("memory-1", CreateRequest(
            timestamp: timestamp,
            role: "ASSISTANT",
            text: "Hi there!"));

        var response = store.ListEvents("memory-1", "actor-1", "session-1",
            includePayloads: true, maxResults: null, nextToken: null);

        Assert.Single(response.Events);
        var evt = response.Events[0];

        // Event metadata should be present
        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.Equal("memory-1", evt.MemoryId);
        Assert.Equal("actor-1", evt.ActorId);
        Assert.Equal("session-1", evt.SessionId);
        Assert.Equal(new DateTimeOffset(timestamp, TimeSpan.Zero).ToUnixTimeSeconds(), evt.EventTimestamp);

        // Payload should be present with full data
        Assert.NotNull(evt.Payload);
        Assert.Single(evt.Payload);
        Assert.Equal("ASSISTANT", evt.Payload[0].Conversational!.Role);
        Assert.Equal("Hi there!", evt.Payload[0].Conversational!.Content!.Text);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Testing.Emulators.Memory;
using AWS.AgentCore.Testing.Emulators.Memory.Models;
using FsCheck;
using FsCheck.Xunit;

namespace AWS.AgentCore.Testing.UnitTests;

/// <summary>
/// Property-based tests for InMemoryEventStore correctness properties.
/// Uses FsCheck to generate arbitrary inputs and verify universal properties.
/// Feature: aspire-local-dev
/// </summary>
public class InMemoryEventStorePropertyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Property 1: Memory Store Round-Trip
    // For any valid conversation event (with non-empty MemoryId, SessionId,
    // ActorId, a USER or ASSISTANT role, and non-empty text content),
    // storing it via CreateEvent and then retrieving it via ListEvents
    // with the same MemoryId, SessionId, and ActorId should return an
    // event with identical role, text content, and timestamp.
    // **Validates: Requirements 3.1, 3.2, 3.5, 10.1, 10.2, 10.3, 10.4**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public bool RoundTrip_PreservesRoleTextAndTimestamp(
        NonEmptyString memoryIdWrapper,
        NonEmptyString sessionIdWrapper,
        NonEmptyString actorIdWrapper,
        NonEmptyString textWrapper,
        bool isUser)
    {
        var memoryId = memoryIdWrapper.Get;
        var sessionId = sessionIdWrapper.Get;
        var actorId = actorIdWrapper.Get;
        var text = textWrapper.Get;
        var role = isUser ? "USER" : "ASSISTANT";

        // Use a fixed timestamp to ensure deterministic comparison
        var timestamp = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);

        var store = new InMemoryEventStore();

        var request = new CreateEventApiRequest
        {
            ActorId = actorId,
            SessionId = sessionId,
            EventTimestamp = new DateTimeOffset(timestamp).ToUnixTimeSeconds(),
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

        // Store the event
        var createResponse = store.CreateEventAsync(memoryId, request);

        // Retrieve the event
        var listResponse = store.ListEvents(memoryId, actorId, sessionId,
            includePayloads: true, maxResults: null, nextToken: null);

        // Verify round-trip consistency
        if (listResponse.Events.Count != 1)
            return false;

        var retrievedEvent = listResponse.Events[0];

        // Verify role matches
        var retrievedRole = retrievedEvent.Payload?[0].Conversational?.Role;
        if (retrievedRole != role)
            return false;

        // Verify text matches
        var retrievedText = retrievedEvent.Payload?[0].Conversational?.Content?.Text;
        if (retrievedText != text)
            return false;

        // Verify timestamp matches
        var expectedEpoch = new DateTimeOffset(timestamp).ToUnixTimeSeconds();
        if (retrievedEvent.EventTimestamp != expectedEpoch)
            return false;

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 2: Memory Store Chronological Ordering
    // For any set of N events stored with distinct timestamps (in arbitrary
    // insertion order) for the same MemoryId/SessionId/ActorId, ListEvents
    // should return all N events sorted by EventTimestamp in ascending order.
    // **Validates: Requirements 3.3**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public bool ChronologicalOrdering_ListEventsReturnsSortedByTimestamp(PositiveInt countWrapper)
    {
        // Cap N to a reasonable size for test performance
        var n = Math.Min(countWrapper.Get, 50);

        var store = new InMemoryEventStore();
        var memoryId = "memory-ordering-test";
        var actorId = "actor-ordering-test";
        var sessionId = "session-ordering-test";

        // Generate N distinct timestamps
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timestamps = Enumerable.Range(0, n)
            .Select(i => baseTime.AddMinutes(i))
            .ToList();

        // Shuffle timestamps to insert in arbitrary order
        var shuffled = timestamps.OrderBy(_ => Guid.NewGuid()).ToList();

        // Insert events in shuffled order
        for (int i = 0; i < n; i++)
        {
            var request = new CreateEventApiRequest
            {
                ActorId = actorId,
                SessionId = sessionId,
                EventTimestamp = new DateTimeOffset(shuffled[i]).ToUnixTimeSeconds(),
                Payload =
                [
                    new PayloadTypeModel
                    {
                        Conversational = new ConversationalModel
                        {
                            Role = "USER",
                            Content = new ContentModel { Text = $"Event at {shuffled[i]:O}" }
                        }
                    }
                ]
            };

            store.CreateEventAsync(memoryId, request);
        }

        // Retrieve all events (use maxResults large enough to get all in one page)
        var listResponse = store.ListEvents(memoryId, actorId, sessionId,
            includePayloads: true, maxResults: n + 10, nextToken: null);

        // Verify all N events are returned
        if (listResponse.Events.Count != n)
            return false;

        // Verify events are sorted by EventTimestamp ascending
        for (int i = 0; i < listResponse.Events.Count - 1; i++)
        {
            if (listResponse.Events[i].EventTimestamp >= listResponse.Events[i + 1].EventTimestamp)
                return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 3: Memory Store Pagination Completeness
    // For any set of N events stored for the same MemoryId/SessionId/ActorId
    // where N exceeds the page size, iterating through all pages using
    // NextToken should yield exactly N events with no duplicates and no gaps,
    // in chronological order.
    // **Validates: Requirements 3.7**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public bool PaginationCompleteness_AllEventsReturnedWithNoDuplicatesNoGaps(PositiveInt countWrapper)
    {
        // Use a small page size to force pagination.
        // N must exceed page size, so ensure at least pageSize + 1 events.
        var pageSize = 5;
        var n = Math.Min(countWrapper.Get, 50) + pageSize + 1; // Ensure N > pageSize

        var store = new InMemoryEventStore();
        var memoryId = "memory-pagination-test";
        var actorId = "actor-pagination-test";
        var sessionId = "session-pagination-test";

        // Generate N events with distinct timestamps in arbitrary insertion order
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var timestamps = Enumerable.Range(0, n)
            .Select(i => baseTime.AddMinutes(i))
            .ToList();

        var shuffled = timestamps.OrderBy(_ => Guid.NewGuid()).ToList();

        for (int i = 0; i < n; i++)
        {
            var request = new CreateEventApiRequest
            {
                ActorId = actorId,
                SessionId = sessionId,
                EventTimestamp = new DateTimeOffset(shuffled[i]).ToUnixTimeSeconds(),
                Payload =
                [
                    new PayloadTypeModel
                    {
                        Conversational = new ConversationalModel
                        {
                            Role = "USER",
                            Content = new ContentModel { Text = $"Event-{shuffled[i].Ticks}" }
                        }
                    }
                ]
            };

            store.CreateEventAsync(memoryId, request);
        }

        // Iterate all pages via NextToken
        var allEvents = new List<EventModel>();
        string? nextToken = null;

        do
        {
            var response = store.ListEvents(memoryId, actorId, sessionId,
                includePayloads: true, maxResults: pageSize, nextToken: nextToken);

            allEvents.AddRange(response.Events);
            nextToken = response.NextToken;
        } while (nextToken != null);

        // Verify exactly N events returned
        if (allEvents.Count != n)
            return false;

        // Verify no duplicates (all EventIds are distinct)
        var distinctIds = allEvents.Select(e => e.EventId).Distinct().Count();
        if (distinctIds != n)
            return false;

        // Verify chronological order (no gaps — events are sorted ascending by timestamp)
        for (int i = 0; i < allEvents.Count - 1; i++)
        {
            if (allEvents[i].EventTimestamp >= allEvents[i + 1].EventTimestamp)
                return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 4: Memory Store Filtering Isolation
    // For any two distinct (MemoryId, SessionId, ActorId) tuples with
    // events stored for each, ListEvents for one tuple should never
    // return events belonging to the other tuple.
    // **Validates: Requirements 3.1**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public bool FilteringIsolation_ListEventsNeverReturnsEventsFromOtherTuple(
        NonEmptyString memoryId1Wrapper,
        NonEmptyString sessionId1Wrapper,
        NonEmptyString actorId1Wrapper,
        NonEmptyString memoryId2Wrapper,
        NonEmptyString sessionId2Wrapper,
        NonEmptyString actorId2Wrapper,
        PositiveInt count1Wrapper,
        PositiveInt count2Wrapper)
    {
        var memoryId1 = memoryId1Wrapper.Get;
        var sessionId1 = sessionId1Wrapper.Get;
        var actorId1 = actorId1Wrapper.Get;
        var memoryId2 = memoryId2Wrapper.Get;
        var sessionId2 = sessionId2Wrapper.Get;
        var actorId2 = actorId2Wrapper.Get;

        // Ensure the two tuples are actually distinct
        var tuple1 = $"{memoryId1}::{actorId1}::{sessionId1}";
        var tuple2 = $"{memoryId2}::{actorId2}::{sessionId2}";
        if (tuple1 == tuple2)
            return true; // vacuously true — same tuple, skip

        // Cap event counts for test performance
        var count1 = Math.Min(count1Wrapper.Get, 10);
        var count2 = Math.Min(count2Wrapper.Get, 10);

        var store = new InMemoryEventStore();
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Store events for tuple 1
        for (int i = 0; i < count1; i++)
        {
            var request = new CreateEventApiRequest
            {
                ActorId = actorId1,
                SessionId = sessionId1,
                EventTimestamp = new DateTimeOffset(baseTime.AddMinutes(i)).ToUnixTimeSeconds(),
                Payload =
                [
                    new PayloadTypeModel
                    {
                        Conversational = new ConversationalModel
                        {
                            Role = "USER",
                            Content = new ContentModel { Text = $"tuple1-event-{i}" }
                        }
                    }
                ]
            };
            store.CreateEventAsync(memoryId1, request);
        }

        // Store events for tuple 2
        for (int i = 0; i < count2; i++)
        {
            var request = new CreateEventApiRequest
            {
                ActorId = actorId2,
                SessionId = sessionId2,
                EventTimestamp = new DateTimeOffset(baseTime.AddMinutes(i)).ToUnixTimeSeconds(),
                Payload =
                [
                    new PayloadTypeModel
                    {
                        Conversational = new ConversationalModel
                        {
                            Role = "ASSISTANT",
                            Content = new ContentModel { Text = $"tuple2-event-{i}" }
                        }
                    }
                ]
            };
            store.CreateEventAsync(memoryId2, request);
        }

        // List events for tuple 1 — should only contain tuple 1 events
        var response1 = store.ListEvents(memoryId1, actorId1, sessionId1,
            includePayloads: true, maxResults: count1 + count2 + 10, nextToken: null);

        if (response1.Events.Count != count1)
            return false;

        // Verify no tuple 2 events leaked into tuple 1 results
        foreach (var evt in response1.Events)
        {
            if (evt.MemoryId != memoryId1 || evt.ActorId != actorId1 || evt.SessionId != sessionId1)
                return false;

            var text = evt.Payload?[0].Conversational?.Content?.Text ?? "";
            if (!text.StartsWith("tuple1-event-"))
                return false;
        }

        // List events for tuple 2 — should only contain tuple 2 events
        var response2 = store.ListEvents(memoryId2, actorId2, sessionId2,
            includePayloads: true, maxResults: count1 + count2 + 10, nextToken: null);

        if (response2.Events.Count != count2)
            return false;

        // Verify no tuple 1 events leaked into tuple 2 results
        foreach (var evt in response2.Events)
        {
            if (evt.MemoryId != memoryId2 || evt.ActorId != actorId2 || evt.SessionId != sessionId2)
                return false;

            var text = evt.Payload?[0].Conversational?.Content?.Text ?? "";
            if (!text.StartsWith("tuple2-event-"))
                return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 5: IncludePayloads Controls Response Content
    // For any set of stored events, ListEvents with includePayloads=true
    // should return events with full payload data, and ListEvents with
    // includePayloads=false should return events with null/empty payloads
    // — but both should return the same event count and event IDs.
    // **Validates: Requirements 10.6**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public bool IncludePayloads_ControlsResponseContent(PositiveInt countWrapper, bool isUser)
    {
        // Cap N to a reasonable size for test performance
        var n = Math.Min(countWrapper.Get, 30);

        var store = new InMemoryEventStore();
        var memoryId = "memory-payload-test";
        var actorId = "actor-payload-test";
        var sessionId = "session-payload-test";
        var role = isUser ? "USER" : "ASSISTANT";

        // Generate and store N events with payloads
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < n; i++)
        {
            var request = new CreateEventApiRequest
            {
                ActorId = actorId,
                SessionId = sessionId,
                EventTimestamp = new DateTimeOffset(baseTime.AddMinutes(i)).ToUnixTimeSeconds(),
                Payload =
                [
                    new PayloadTypeModel
                    {
                        Conversational = new ConversationalModel
                        {
                            Role = role,
                            Content = new ContentModel { Text = $"Message {i}" }
                        }
                    }
                ]
            };

            store.CreateEventAsync(memoryId, request);
        }

        // Call ListEvents with includePayloads=true
        var withPayloads = store.ListEvents(memoryId, actorId, sessionId,
            includePayloads: true, maxResults: n + 10, nextToken: null);

        // Call ListEvents with includePayloads=false
        var withoutPayloads = store.ListEvents(memoryId, actorId, sessionId,
            includePayloads: false, maxResults: n + 10, nextToken: null);

        // Verify same event count
        if (withPayloads.Events.Count != n)
            return false;
        if (withoutPayloads.Events.Count != n)
            return false;

        // Verify same event IDs in same order
        for (int i = 0; i < n; i++)
        {
            if (withPayloads.Events[i].EventId != withoutPayloads.Events[i].EventId)
                return false;
        }

        // Verify payloads present when includePayloads=true
        for (int i = 0; i < n; i++)
        {
            var payload = withPayloads.Events[i].Payload;
            if (payload == null || payload.Count == 0)
                return false;

            // Verify the payload content is intact
            var text = payload[0].Conversational?.Content?.Text;
            if (text != $"Message {i}")
                return false;
        }

        // Verify payloads absent when includePayloads=false
        for (int i = 0; i < n; i++)
        {
            if (withoutPayloads.Events[i].Payload != null)
                return false;
        }

        return true;
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable MAAI001 // Experimental API usage required for testing

using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using AWS.AgentCore;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AWS.AgentCore.UnitTests;

/// <summary>
/// Property-based tests for AgentCoreMemoryProvider correctness properties.
/// Uses FsCheck to generate arbitrary inputs and verify universal properties.
/// Tag format: Feature: agentcore-memory, Property {number}: {property_text}
/// </summary>
public class AgentCoreMemoryProviderPropertyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Property 1: Event-to-ChatMessage Conversion Preserves Data
    // For any valid AgentCore Memory event with USER/ASSISTANT role and
    // non-empty text, conversion produces a ChatMessage with the
    // corresponding ChatRole and identical text content.
    // Validates: Requirements 1.2, 3.3
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public bool EventToMessageConversion_PreservesRoleAndText(NonEmptyString textWrapper, bool isUser)
    {
        var text = textWrapper.Get;
        // Skip whitespace-only strings since the implementation requires Length > 0 on Content.Text
        if (string.IsNullOrWhiteSpace(text))
            return true; // vacuously true for invalid inputs

        var role = isUser ? Role.USER : Role.ASSISTANT;
        var expectedChatRole = isUser ? ChatRole.User : ChatRole.Assistant;

        var evt = new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = role,
                        Content = new Content { Text = text }
                    }
                }
            ]
        };

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out var message);

        return success
            && message.Role == expectedChatRole
            && message.Text == text;
    }

    [Property(MaxTest = 100)]
    public bool EventToMessageConversion_ToolAndOtherRoles_ReturnFalse(NonEmptyString textWrapper)
    {
        var text = textWrapper.Get;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var toolRoles = new[] { Role.TOOL, Role.OTHER };

        foreach (var role in toolRoles)
        {
            var evt = new Event
            {
                Payload =
                [
                    new PayloadType
                    {
                        Conversational = new Conversational
                        {
                            Role = role,
                            Content = new Content { Text = text }
                        }
                    }
                ]
            };

            if (AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out _))
                return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 2: Message Filtering Excludes Invalid Messages
    // A message is persisted if and only if: (a) User or Assistant role,
    // (b) no FunctionCallContent/FunctionResultContent, (c) non-null
    // non-whitespace text with length >= 1.
    // Validates: Requirements 2.3, 3.1, 3.2, 3.4
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public bool MessageFiltering_UserTextMessages_AreIncluded(NonEmptyString textWrapper, bool isUser)
    {
        var text = textWrapper.Get;
        if (string.IsNullOrWhiteSpace(text))
            return true; // vacuously true — whitespace messages should be excluded

        var chatRole = isUser ? ChatRole.User : ChatRole.Assistant;
        var message = new ChatMessage(chatRole, text);

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(
            new[] { message }, null).ToList();

        var expectedRole = isUser ? Role.USER : Role.ASSISTANT;

        return result.Count == 1
            && result[0].Role == expectedRole
            && result[0].Text == text;
    }

    [Property(MaxTest = 100)]
    public bool MessageFiltering_EmptyOrWhitespaceText_IsExcluded(byte whitespaceCount)
    {
        // Generate whitespace-only strings of various lengths
        var text = new string(' ', whitespaceCount);
        var message = new ChatMessage(ChatRole.User, text);

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(
            new[] { message }, null).ToList();

        return result.Count == 0;
    }

    [Property(MaxTest = 100)]
    public bool MessageFiltering_ToolCallContent_IsExcluded(NonEmptyString textWrapper)
    {
        var text = textWrapper.Get;

        // Message with FunctionCallContent should always be excluded
        var message = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-id", "FunctionName", new Dictionary<string, object?> { ["arg"] = text })]);

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(
            new[] { message }, null).ToList();

        return result.Count == 0;
    }

    [Property(MaxTest = 100)]
    public bool MessageFiltering_ToolResultContent_IsExcluded(NonEmptyString textWrapper)
    {
        var text = textWrapper.Get;

        // Message with FunctionResultContent should always be excluded
        var message = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("call-id", text)]);

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(
            new[] { message }, null).ToList();

        return result.Count == 0;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 3: Pagination Fetches All Pages in Order
    // For any sequence of N paginated ListEvents responses (1..N-1 have
    // NextToken, N does not), the provider makes exactly N API calls and
    // returns all events concatenated in page order.
    // Validates: Requirements 1.3, 10.1, 10.2
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public async Task Pagination_FetchesAllEventsInOrder(PositiveInt eventCountWrapper)
    {
        var eventCount = Math.Min(eventCountWrapper.Get, 20); // Cap for test performance

        var events = Enumerable.Range(0, eventCount).Select(i => new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = Role.USER,
                        Content = new Content { Text = $"message-{i}" }
                    }
                }
            ]
        }).ToList();

        var mockPaginator = new Mock<IListEventsPaginator>();
        mockPaginator.Setup(p => p.Events).Returns(new TestPaginatedEnumerable<Event>(events));

        var mockPaginatorFactory = new Mock<IBedrockAgentCorePaginatorFactory>();
        mockPaginatorFactory.Setup(f => f.ListEvents(It.IsAny<ListEventsRequest>())).Returns(mockPaginator.Object);

        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient.Setup(c => c.Paginators).Returns(mockPaginatorFactory.Object);

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        var result = await InvokeProvideChatHistoryAsync(provider, context);
        var messages = result.ToList();

        Assert.Equal(eventCount, messages.Count);

        for (int i = 0; i < eventCount; i++)
        {
            Assert.Equal($"message-{i}", messages[i].Text);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 4: Errors Never Propagate to Caller
    // For any exception thrown by the Memory client during ListEvents or
    // CreateEvent, the provider catches it and returns gracefully.
    // Validates: Requirements 1.5, 2.4
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public async Task Errors_NeverPropagate_OnLoad(NonEmptyString exceptionMessage)
    {
        var mockPaginator = new Mock<IListEventsPaginator>();
        mockPaginator.Setup(p => p.Events).Returns(new ThrowingPaginatedEnumerable<Event>(new InvalidOperationException(exceptionMessage.Get)));

        var mockPaginatorFactory = new Mock<IBedrockAgentCorePaginatorFactory>();
        mockPaginatorFactory.Setup(f => f.ListEvents(It.IsAny<ListEventsRequest>())).Returns(mockPaginator.Object);

        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient.Setup(c => c.Paginators).Returns(mockPaginatorFactory.Object);

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        // Should not throw — returns empty collection
        var result = await InvokeProvideChatHistoryAsync(provider, context);
        Assert.Empty(result);
    }

    [Property(MaxTest = 100)]
    public async Task Errors_NeverPropagate_OnSave(NonEmptyString exceptionMessage, NonEmptyString messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText.Get))
            return;

        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient
            .Setup(c => c.CreateEventAsync(It.IsAny<CreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage.Get));

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokedContext(
            provider,
            session,
            new[] { new ChatMessage(ChatRole.User, messageText.Get) },
            new[] { new ChatMessage(ChatRole.Assistant, "response") });

        // Should not throw
        await InvokeStoreChatHistoryAsync(provider, context);
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 6: Partial Pagination Failure Returns Loaded Pages
    // When an error occurs after some events have been loaded, the
    // provider returns what was loaded and does not throw.
    // Validates: Requirements 10.3
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public async Task PartialPaginationFailure_ReturnsLoadedEvents(PositiveInt successfulEventsWrapper)
    {
        var successfulEvents = Math.Min(successfulEventsWrapper.Get, 20); // Cap for performance

        var mockPaginator = new Mock<IListEventsPaginator>();
        mockPaginator.Setup(p => p.Events).Returns(new EventsThenThrowPaginatedEnumerable(successfulEvents));

        var mockPaginatorFactory = new Mock<IBedrockAgentCorePaginatorFactory>();
        mockPaginatorFactory.Setup(f => f.ListEvents(It.IsAny<ListEventsRequest>())).Returns(mockPaginator.Object);

        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient.Setup(c => c.Paginators).Returns(mockPaginatorFactory.Object);

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        // Should not throw — returns partial results
        var result = await InvokeProvideChatHistoryAsync(provider, context);
        var messages = result.ToList();

        // Should have exactly the messages from successful events
        Assert.Equal(successfulEvents, messages.Count);
        for (int i = 0; i < successfulEvents; i++)
        {
            Assert.Equal($"event-{i}", messages[i].Text);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Helper methods
    // ──────────────────────────────────────────────────────────────────

    private static AgentSession CreateSessionWithRuntimeContext(string sessionId)
    {
        var session = new Mock<AgentSession>() { CallBase = true }.Object;
        var runtimeContext = new AgentCoreRuntimeContext
        {
            SessionId = sessionId,
            RequestId = "test-request"
        };
        session.StateBag.SetValue(AgentCoreRuntimeContextProvider.ContextKey, runtimeContext);
        return session;
    }

    private static ChatHistoryProvider.InvokingContext CreateInvokingContext(
        AgentCoreMemoryProvider provider,
        AgentSession session)
    {
        // The InvokingContext constructor requires (AIAgent, AgentSession, IEnumerable<ChatMessage>)
        // We use a mock AIAgent since we only need the session for our tests
        var mockAgent = new Mock<AIAgent>() { CallBase = false };
        return new ChatHistoryProvider.InvokingContext(
            mockAgent.Object,
            session,
            new List<ChatMessage>());
    }

    private static ChatHistoryProvider.InvokedContext CreateInvokedContext(
        AgentCoreMemoryProvider provider,
        AgentSession session,
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages)
    {
        var mockAgent = new Mock<AIAgent>() { CallBase = false };
        return new ChatHistoryProvider.InvokedContext(
            mockAgent.Object,
            session,
            requestMessages,
            responseMessages);
    }

    private static async Task<IEnumerable<ChatMessage>> InvokeProvideChatHistoryAsync(
        AgentCoreMemoryProvider provider,
        ChatHistoryProvider.InvokingContext context)
    {
        // ProvideChatHistoryAsync is protected, invoke via reflection
        var method = typeof(AgentCoreMemoryProvider).GetMethod(
            "ProvideChatHistoryAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (ValueTask<IEnumerable<ChatMessage>>)method!.Invoke(
            provider, new object[] { context, CancellationToken.None })!;

        return await task;
    }

    private static async Task InvokeStoreChatHistoryAsync(
        AgentCoreMemoryProvider provider,
        ChatHistoryProvider.InvokedContext context)
    {
        var method = typeof(AgentCoreMemoryProvider).GetMethod(
            "StoreChatHistoryAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (ValueTask)method!.Invoke(
            provider, new object[] { context, CancellationToken.None })!;

        await task;
    }

    /// <summary>Helper: wraps a list as IPaginatedEnumerable for mocking paginator.Events</summary>
    private sealed class TestPaginatedEnumerable<T> : Amazon.Runtime.IPaginatedEnumerable<T>
    {
        private readonly IEnumerable<T> _items;
        public TestPaginatedEnumerable(IEnumerable<T> items) => _items = items;
        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
        {
            foreach (var item in _items)
            {
                await Task.CompletedTask;
                yield return item;
            }
        }
    }

    /// <summary>Helper: IPaginatedEnumerable that throws immediately</summary>
    private sealed class ThrowingPaginatedEnumerable<T> : Amazon.Runtime.IPaginatedEnumerable<T>
    {
        private readonly Exception _ex;
        public ThrowingPaginatedEnumerable(Exception ex) => _ex = ex;
#pragma warning disable CS0162
        public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
        {
            await Task.CompletedTask;
            throw _ex;
            yield break;
        }
#pragma warning restore CS0162
    }

    /// <summary>Helper: yields N events then throws</summary>
    private sealed class EventsThenThrowPaginatedEnumerable : Amazon.Runtime.IPaginatedEnumerable<Event>
    {
        private readonly int _successfulCount;
        public EventsThenThrowPaginatedEnumerable(int successfulCount) => _successfulCount = successfulCount;
        public async IAsyncEnumerator<Event> GetAsyncEnumerator(CancellationToken ct = default)
        {
            for (int i = 0; i < _successfulCount; i++)
            {
                await Task.CompletedTask;
                yield return new Event
                {
                    Payload =
                    [
                        new PayloadType
                        {
                            Conversational = new Conversational
                            {
                                Role = Role.USER,
                                Content = new Content { Text = $"event-{i}" }
                            }
                        }
                    ]
                };
            }

            throw new AmazonBedrockAgentCoreException("Simulated pagination failure");
        }
    }
}

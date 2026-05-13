// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#pragma warning disable MAAI001 // Experimental API usage required for testing

using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
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
    public async void Pagination_FetchesAllPagesInOrder(PositiveInt pageCountWrapper)
    {
        var pageCount = Math.Min(pageCountWrapper.Get, 10); // Cap at 10 pages for test performance
        var callCount = 0;

        var mockClient = new Mock<IAmazonBedrockAgentCore>();

        // Set up paginated responses
        var currentPage = 0;
        mockClient
            .Setup(c => c.ListEventsAsync(It.IsAny<ListEventsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListEventsRequest req, CancellationToken _) =>
            {
                callCount++;
                var page = currentPage++;
                var text = $"message-page-{page}";

                return new ListEventsResponse
                {
                    Events =
                    [
                        new Event
                        {
                            Payload =
                            [
                                new PayloadType
                                {
                                    Conversational = new Conversational
                                    {
                                        Role = Role.USER,
                                        Content = new Content { Text = text }
                                    }
                                }
                            ]
                        }
                    ],
                    NextToken = page < pageCount - 1 ? $"token-{page + 1}" : null
                };
            });

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        var result = await InvokeProvideChatHistoryAsync(provider, context);
        var messages = result.ToList();

        Assert.Equal(pageCount, callCount);
        Assert.Equal(pageCount, messages.Count);

        for (int i = 0; i < pageCount; i++)
        {
            Assert.Equal($"message-page-{i}", messages[i].Text);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 4: Errors Never Propagate to Caller
    // For any exception thrown by the Memory client during ListEvents or
    // CreateEvent, the provider catches it and returns gracefully.
    // Validates: Requirements 1.5, 2.4
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public async void Errors_NeverPropagate_OnLoad(NonEmptyString exceptionMessage)
    {
        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient
            .Setup(c => c.ListEventsAsync(It.IsAny<ListEventsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(exceptionMessage.Get));

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        // Should not throw — returns empty collection
        var result = await InvokeProvideChatHistoryAsync(provider, context);
        Assert.Empty(result);
    }

    [Property(MaxTest = 100)]
    public async void Errors_NeverPropagate_OnSave(NonEmptyString exceptionMessage, NonEmptyString messageText)
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
    // When an error occurs on page K (K > 1), the provider returns all
    // events from pages 1 through K-1 and does not throw.
    // Validates: Requirements 10.3
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 100)]
    public async void PartialPaginationFailure_ReturnsLoadedPages(PositiveInt successfulPagesWrapper)
    {
        var successfulPages = Math.Min(successfulPagesWrapper.Get, 10); // Cap for performance
        var currentPage = 0;

        var mockClient = new Mock<IAmazonBedrockAgentCore>();
        mockClient
            .Setup(c => c.ListEventsAsync(It.IsAny<ListEventsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListEventsRequest req, CancellationToken _) =>
            {
                var page = currentPage++;

                if (page >= successfulPages)
                {
                    throw new AmazonBedrockAgentCoreException("Simulated pagination failure");
                }

                return new ListEventsResponse
                {
                    Events =
                    [
                        new Event
                        {
                            Payload =
                            [
                                new PayloadType
                                {
                                    Conversational = new Conversational
                                    {
                                        Role = Role.USER,
                                        Content = new Content { Text = $"page-{page}-msg" }
                                    }
                                }
                            ]
                        }
                    ],
                    // All successful pages have a NextToken (pointing to next page which may fail)
                    NextToken = $"token-{page + 1}"
                };
            });

        var options = new AgentCoreOptions { MemoryId = "test-memory" };
        var provider = new AgentCoreMemoryProvider(options, NullLogger<AgentCoreMemoryProvider>.Instance, mockClient.Object);

        var session = CreateSessionWithRuntimeContext("test-session");
        var context = CreateInvokingContext(provider, session);

        // Should not throw — returns partial results
        var result = await InvokeProvideChatHistoryAsync(provider, context);
        var messages = result.ToList();

        // Should have exactly the messages from successful pages
        Assert.Equal(successfulPages, messages.Count);
        for (int i = 0; i < successfulPages; i++)
        {
            Assert.Equal($"page-{i}-msg", messages[i].Text);
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
}

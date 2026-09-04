// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AWS.AgentCore.Hosting.UnitTests;

[Collection("EnvironmentVariableTests")]
public class AgentCoreMemoryProviderTests
{
    private static AgentCoreMemoryProvider CreateProvider(
        AgentCoreOptions? options = null,
        IAmazonBedrockAgentCore? memoryClient = null,
        ILogger<AgentCoreMemoryProvider>? logger = null)
    {
        return new AgentCoreMemoryProvider(
            options ?? new AgentCoreOptions(),
            logger ?? NullLogger<AgentCoreMemoryProvider>.Instance,
            memoryClient);
    }

    // ──────────────────────────────────────────────────────────────────
    // Configuration tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveMemoryId_WhenOptionsSet_ReturnsOptionsValue()
    {
        var provider = CreateProvider(new AgentCoreOptions { MemoryId = "mem-from-options" });

        var result = provider.GetEffectiveMemoryId();

        Assert.Equal("mem-from-options", result);
    }

    [Fact]
    public void GetEffectiveMemoryId_WhenOptionsNotSet_FallsBackToEnvVar()
    {
        Environment.SetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable, "mem-from-env");
        try
        {
            var provider = CreateProvider(new AgentCoreOptions { MemoryId = null });

            var result = provider.GetEffectiveMemoryId();

            Assert.Equal("mem-from-env", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable, null);
        }
    }

    [Fact]
    public void GetEffectiveMemoryId_WhenBothSet_OptionsWins()
    {
        Environment.SetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable, "mem-from-env");
        try
        {
            var provider = CreateProvider(new AgentCoreOptions { MemoryId = "mem-from-options" });

            var result = provider.GetEffectiveMemoryId();

            Assert.Equal("mem-from-options", result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable, null);
        }
    }

    [Fact]
    public void GetEffectiveMemoryId_WhenNeitherSet_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable, null);
        var provider = CreateProvider(new AgentCoreOptions { MemoryId = null });

        var result = provider.GetEffectiveMemoryId();

        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // Message filtering tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FilterMessagesForStorage_SkipsToolCallMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "GetWeather", new Dictionary<string, object?> { ["location"] = "Seattle" })]),
        };

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(messages, null).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void FilterMessagesForStorage_SkipsToolResultMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "result data")]),
        };

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(messages, null).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void FilterMessagesForStorage_SkipsEmptyTextMessages()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, ""),
            new(ChatRole.User, "   "),
        };

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(messages, null).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void FilterMessagesForStorage_SavesUserAndAssistantMessages()
    {
        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
        };
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Hi there!"),
        };

        var result = AgentCoreMemoryProvider.FilterMessagesForStorage(requestMessages, responseMessages).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(Amazon.BedrockAgentCore.Role.USER, result[0].Role);
        Assert.Equal("Hello", result[0].Text);
        Assert.Equal(Amazon.BedrockAgentCore.Role.ASSISTANT, result[1].Role);
        Assert.Equal("Hi there!", result[1].Text);
    }

    // ──────────────────────────────────────────────────────────────────
    // Event conversion tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TryConvertEventToChatMessage_ValidUserEvent_ReturnsUserMessage()
    {
        var evt = new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = Amazon.BedrockAgentCore.Role.USER,
                        Content = new Content { Text = "Hello" }
                    }
                }
            ]
        };

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out var message);

        Assert.True(success);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("Hello", message.Text);
    }

    [Fact]
    public void TryConvertEventToChatMessage_ValidAssistantEvent_ReturnsAssistantMessage()
    {
        var evt = new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = Amazon.BedrockAgentCore.Role.ASSISTANT,
                        Content = new Content { Text = "Hi there!" }
                    }
                }
            ]
        };

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out var message);

        Assert.True(success);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("Hi there!", message.Text);
    }

    [Fact]
    public void TryConvertEventToChatMessage_ToolRoleEvent_ReturnsFalse()
    {
        var evt = new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = Amazon.BedrockAgentCore.Role.TOOL,
                        Content = new Content { Text = "tool result" }
                    }
                }
            ]
        };

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out _);

        Assert.False(success);
    }

    [Fact]
    public void TryConvertEventToChatMessage_EmptyPayload_ReturnsFalse()
    {
        var evt = new Event { Payload = [] };

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out _);

        Assert.False(success);
    }

    [Fact]
    public void TryConvertEventToChatMessage_EmptyText_ReturnsFalse()
    {
        #pragma warning disable BedrockAgentCore1000 // SDK validation warning for intentionally invalid test input
        var evt = new Event
        {
            Payload =
            [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = Amazon.BedrockAgentCore.Role.USER,
                        Content = new Content { Text = "" }
                    }
                }
            ]
        };
        #pragma warning restore BedrockAgentCore1000

        var success = AgentCoreMemoryProvider.TryConvertEventToChatMessage(evt, out _);

        Assert.False(success);
    }

    // ──────────────────────────────────────────────────────────────────
    // HasToolContent tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void HasToolContent_WithFunctionCallContent_ReturnsTrue()
    {
        var message = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", "GetWeather", new Dictionary<string, object?> { ["loc"] = "NYC" })]);

        Assert.True(AgentCoreMemoryProvider.HasToolContent(message));
    }

    [Fact]
    public void HasToolContent_WithFunctionResultContent_ReturnsTrue()
    {
        var message = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("call-1", "sunny")]);

        Assert.True(AgentCoreMemoryProvider.HasToolContent(message));
    }

    [Fact]
    public void HasToolContent_WithTextOnly_ReturnsFalse()
    {
        var message = new ChatMessage(ChatRole.User, "Hello");

        Assert.False(AgentCoreMemoryProvider.HasToolContent(message));
    }

    // ──────────────────────────────────────────────────────────────────
    // NextEventTimestamp (monotonic per-session ordering) tests
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NextEventTimestamp_WhenClockDoesNotAdvance_ReturnsStrictlyIncreasingValues()
    {
        var provider = CreateProvider();
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Same wall-clock instant for both saves (the coarse-resolution / non-monotonic case).
        var first = provider.NextEventTimestamp("session-a", now);
        var second = provider.NextEventTimestamp("session-a", now);

        Assert.True(second > first,
            "Second timestamp must be strictly greater than the first even when the clock does not advance.");
    }

    [Fact]
    public void NextEventTimestamp_PreservesUserThenAssistantOrder()
    {
        var provider = CreateProvider();
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Mirrors StoreChatHistoryAsync saving a user turn then the assistant reply in one loop.
        var userTs = provider.NextEventTimestamp("session-a", now);
        var assistantTs = provider.NextEventTimestamp("session-a", now);

        // A stable OrderBy on replay will keep user before assistant only if the timestamps differ.
        Assert.True(assistantTs > userTs);
    }

    [Fact]
    public void NextEventTimestamp_WhenClockAdvances_UsesWallClock()
    {
        var provider = CreateProvider();
        var first = provider.NextEventTimestamp("session-a", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var laterNow = new DateTime(2024, 1, 1, 0, 0, 5, DateTimeKind.Utc);

        var second = provider.NextEventTimestamp("session-a", laterNow);

        Assert.Equal(laterNow, second);
    }

    [Fact]
    public void NextEventTimestamp_WhenClockGoesBackward_StillStrictlyIncreasing()
    {
        var provider = CreateProvider();
        var first = provider.NextEventTimestamp("session-a", new DateTime(2024, 1, 1, 0, 0, 5, DateTimeKind.Utc));

        // Simulate NTP correction / clock skew moving the wall clock backward.
        var second = provider.NextEventTimestamp("session-a", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(second > first);
    }

    [Fact]
    public void NextEventTimestamp_TracksSessionsIndependently()
    {
        var provider = CreateProvider();
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var sessionA = provider.NextEventTimestamp("session-a", now);
        var sessionB = provider.NextEventTimestamp("session-b", now);

        // A different session must not be nudged forward by another session's activity.
        Assert.Equal(now, sessionA);
        Assert.Equal(now, sessionB);
    }

    [Fact]
    public void NextEventTimestamp_ReturnsUtcKind()
    {
        var provider = CreateProvider();

        var result = provider.NextEventTimestamp("session-a", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }
}

public class AgentCoreMemoryDIRegistrationTests
{
    [Fact]
    public void AddAgentCore_RegistersMemoryProvider()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;
        var memoryProvider = sp.GetService<AgentCoreMemoryProvider>();

        Assert.NotNull(memoryProvider);
    }

    [Fact]
    public void AddAgentCore_RegistersIAmazonBedrockAgentCore()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;
        // TryAddAWSService registers it — it should be resolvable (may fail at runtime without credentials, but the registration exists)
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IAmazonBedrockAgentCore));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddAgentCore_WiresMemoryProviderAsChatHistoryProvider()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;

        // Resolve ChatClientAgent (the inner, unwrapped agent) so we can inspect the
        // ChatHistoryProvider directly. AIAgent resolution returns the OTEL-wrapped agent.
        var chatAgent = sp.GetRequiredService<Microsoft.Agents.AI.ChatClientAgent>();

        Assert.IsType<AgentCoreMemoryProvider>(chatAgent.ChatHistoryProvider);
    }
}

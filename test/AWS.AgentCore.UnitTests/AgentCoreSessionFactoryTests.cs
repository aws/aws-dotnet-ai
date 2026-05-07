// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Internal;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Moq;

namespace AWS.AgentCore.UnitTests;

public class AgentCoreSessionFactoryTests
{
    [Fact]
    public async Task CreateSessionAsync_WithSessionId_PassesSessionIdToAgent()
    {
        var mockChatClient = new Mock<IChatClient>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions());
        var context = new AgentCoreRuntimeContext { SessionId = "test-session-123" };

        var session = await AgentCoreSessionFactory.CreateSessionAsync(agent, context, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        // Verify runtime context is stored in state bag
        var storedContext = session.StateBag.GetValue<AgentCoreRuntimeContext>(AgentCoreRuntimeContextProvider.ContextKey);
        Assert.NotNull(storedContext);
        Assert.Equal("test-session-123", storedContext.SessionId);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNullSessionId_CreatesSessionWithoutConversationId()
    {
        var mockChatClient = new Mock<IChatClient>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions());
        var context = new AgentCoreRuntimeContext { SessionId = null };

        var session = await AgentCoreSessionFactory.CreateSessionAsync(agent, context, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
    }

    [Fact]
    public async Task CreateSessionAsync_WithNullContext_DoesNotStoreInStateBag()
    {
        var mockChatClient = new Mock<IChatClient>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions());

        var session = await AgentCoreSessionFactory.CreateSessionAsync(agent, null, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        var storedContext = session.StateBag.GetValue<AgentCoreRuntimeContext>(AgentCoreRuntimeContextProvider.ContextKey);
        Assert.Null(storedContext);
    }

    [Fact]
    public async Task CreateSessionAsync_WithEmptySessionId_CreatesSessionWithoutConversationId()
    {
        var mockChatClient = new Mock<IChatClient>();
        var agent = new ChatClientAgent(mockChatClient.Object, new ChatClientAgentOptions());
        var context = new AgentCoreRuntimeContext { SessionId = "" };

        var session = await AgentCoreSessionFactory.CreateSessionAsync(agent, context, TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        // Context is still stored even with empty session ID
        var storedContext = session.StateBag.GetValue<AgentCoreRuntimeContext>(AgentCoreRuntimeContextProvider.ContextKey);
        Assert.NotNull(storedContext);
    }
}

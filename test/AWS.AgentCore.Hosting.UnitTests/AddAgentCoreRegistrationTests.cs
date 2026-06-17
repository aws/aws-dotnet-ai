// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockRuntime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace AWS.AgentCore.Hosting.UnitTests;

public class AddAgentCoreRegistrationTests
{
    [Fact]
    public void AddAgentCore_WithModelId_RegistersBedrockIChatClient()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(options =>
        {
            options.ModelId = "anthropic.claude-sonnet-4-20250514-v1:0";
        });

        var sp = builder.Build().Services;
        var chatClient = sp.GetService<IChatClient>();

        Assert.NotNull(chatClient);
    }

    [Fact]
    public void AddAgentCore_WithChatClient_WrapsExplicitClientWithOpenTelemetry()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;
        var resolved = sp.GetService<IChatClient>();

        // The resolved client is wrapped with the OpenTelemetry chat client decorator,
        // which delegates to the explicit client provided in options.
        Assert.NotNull(resolved);
        Assert.Equal("OpenTelemetryChatClient", resolved.GetType().Name);
    }

    [Fact]
    public void AddAgentCore_WithBothChatClientAndModelId_ChatClientWins()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ModelId = "anthropic.claude-sonnet-4-20250514-v1:0";
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;
        var resolved = sp.GetService<IChatClient>();

        // Wrapped by OpenTelemetry but the explicit ChatClient wins over ModelId.
        Assert.NotNull(resolved);
        Assert.Equal("OpenTelemetryChatClient", resolved.GetType().Name);
        // Bedrock runtime should NOT be registered when ChatClient is provided
        var bedrockClient = sp.GetService<IAmazonBedrockRuntime>();
        Assert.Null(bedrockClient);
    }

    [Fact]
    public void AddAgentCore_WithPreRegisteredIChatClient_DoesNotOverwrite()
    {
        var builder = WebApplication.CreateBuilder();
        var preRegisteredClient = new Mock<IChatClient>();

        // Pre-register IChatClient before calling AddAgentCore
        builder.Services.AddSingleton<IChatClient>(preRegisteredClient.Object);

        builder.AddAgentCore(); // No ModelId, no ChatClient

        var sp = builder.Build().Services;
        var resolved = sp.GetService<IChatClient>();

        Assert.NotNull(resolved);
        Assert.Same(preRegisteredClient.Object, resolved);
    }

    [Fact]
    public void AddAgentCore_WithNoIChatClient_ThrowsOnResolution()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(); // No ModelId, no ChatClient, no pre-registered

        var sp = builder.Build().Services;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            sp.GetRequiredService<ChatClientAgent>());

        Assert.Contains("No IChatClient is registered", exception.Message);
        Assert.Contains("options.ChatClient", exception.Message);
        Assert.Contains("options.ModelId", exception.Message);
    }

    [Fact]
    public void AddAgentCore_WithAgentOptions_PassesToChatClientAgent()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();
        var agentOptions = new ChatClientAgentOptions
        {
            ChatOptions = new() { Instructions = "You are a helpful assistant." }
        };

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
            options.AgentOptions = agentOptions;
        });

        var sp = builder.Build().Services;
        var agent = sp.GetRequiredService<ChatClientAgent>();

        Assert.NotNull(agent);
    }

    [Fact]
    public void AddAgentCore_WithConfigureAgent_AppliesCallback()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();
        var callbackInvoked = false;

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
            options.ConfigureAgent = agent =>
            {
                callbackInvoked = true;
                return agent;
            };
        });

        var sp = builder.Build().Services;
        // ConfigureAgent only fires when AIAgent is resolved (not when ChatClientAgent is
        // resolved directly — see the trade-off documented in AgentCoreBuilderExtensions).
        var agent = sp.GetRequiredService<AIAgent>();

        Assert.NotNull(agent);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void AddAgentCore_WithModelIdOnly_BackwardCompatible()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(options =>
        {
            options.ModelId = "anthropic.claude-sonnet-4-20250514-v1:0";
        });

        var sp = builder.Build().Services;

        // Bedrock runtime should be registered
        var bedrockClient = sp.GetService<IAmazonBedrockRuntime>();
        Assert.NotNull(bedrockClient);

        // IChatClient should be registered
        var chatClient = sp.GetService<IChatClient>();
        Assert.NotNull(chatClient);

        // AgentCoreOptions should be registered with the model ID
        var options = sp.GetRequiredService<AgentCoreOptions>();
        Assert.Equal("anthropic.claude-sonnet-4-20250514-v1:0", options.ModelId);
    }

    [Fact]
    public void AddAgentCore_RegistersChatClientAgent()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();

        builder.AddAgentCore(options =>
        {
            options.ChatClient = mockClient.Object;
        });

        var sp = builder.Build().Services;
        var agent = sp.GetService<ChatClientAgent>();

        Assert.NotNull(agent);
    }

    [Fact]
    public void AddAgentCore_RegistersAgentCoreRuntimeContextProvider()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore();

        var sp = builder.Build().Services;
        var provider = sp.GetService<AgentCoreRuntimeContextProvider>();

        Assert.NotNull(provider);
    }

    [Fact]
    public void AddAgentCore_WithNoConfig_DoesNotThrowAtRegistration()
    {
        var builder = WebApplication.CreateBuilder();

        // Should not throw during registration (lazy failure)
        var exception = Record.Exception(() =>
        {
            builder.AddAgentCore();
            builder.Build(); // Building the service provider should also not throw
        });

        Assert.Null(exception);
    }
}

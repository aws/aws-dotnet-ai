// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.AgentCore.Hosting.UnitTests;

public class AgentCoreBuilderExtensionsTests
{
    [Fact]
    public void AddAgentCore_RegistersAgentCoreOptions()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(options => options.ModelId = "test-model");

        var sp = builder.Build().Services;
        var options = sp.GetRequiredService<AgentCoreOptions>();

        Assert.NotNull(options);
        Assert.Equal(8080, options.Port);
    }

    [Fact]
    public void AddAgentCore_AppliesConfigureCallback()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(options =>
        {
            options.ModelId = "my-custom-model";
            options.Port = 9090;
        });

        var sp = builder.Build().Services;
        var options = sp.GetRequiredService<AgentCoreOptions>();

        Assert.Equal("my-custom-model", options.ModelId);
        Assert.Equal(9090, options.Port);
    }

    [Fact]
    public void AddAgentCore_RegistersBedrockRuntimeClient()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddAgentCore(options => options.ModelId = "test-model");

        var sp = builder.Build().Services;
        var bedrockClient = sp.GetService<IAmazonBedrockRuntime>();

        Assert.NotNull(bedrockClient);
    }

    [Fact]
    public void AddAgentCore_RegistersIChatClient()
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
    public void AddAgentCore_WithoutModelId_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() => builder.AddAgentCore());

        Assert.Null(exception);
    }

    [Fact]
    public void AddAgentCore_WithNullConfigure_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() => builder.AddAgentCore(null));

        Assert.Null(exception);
    }
}

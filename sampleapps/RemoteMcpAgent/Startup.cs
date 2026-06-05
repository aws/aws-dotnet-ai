// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting;
using AWS.AgentCore.Hosting.Extensions;
using Microsoft.Agents.AI;

namespace RemoteMcpAgent;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddAgentCore(options =>
        {
            options.ModelId = "global.anthropic.claude-opus-4-7";
            options.AgentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new()
                {
                    Instructions = "You are a helpful assistant with access to external tools via MCP. " +
                                   "Use the available tools to answer user questions."
                }
            };
        });

        builder.Services.AddSingleton<McpToolProvider>();
    }
}

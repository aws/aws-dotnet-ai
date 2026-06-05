// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Hosting;
using AWS.AgentCore.Hosting.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace NativeAotAnnotations;

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
                    Tools =
                    [
                        AIFunctionFactory.Create(Agent.GetWeather),
                        AIFunctionFactory.Create(Agent.GetAppInfo)
                    ]
                }
            };
        });
    }
}

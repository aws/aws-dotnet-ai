// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using Microsoft.AspNetCore.Builder;

namespace AnnotationsSample;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddAgentCore(options =>
        {
            options.ModelId = "global.anthropic.claude-opus-4-7";
        });
    }
}

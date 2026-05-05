// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.CloudWatchLogs;
using Amazon.CodeBuild;
using Amazon.SecretsManager;
using AWS.AgentCore;
using AWS.AgentCore.Extensions;
using BuildSystemAgent.Services;
using Microsoft.AspNetCore.Builder;

namespace BuildSystemAgent;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddAgentCore(options =>
        {
            options.ModelId = "global.anthropic.claude-opus-4-7";
        });

        builder.Services.AddAWSService<IAmazonCodeBuild>();
        builder.Services.AddAWSService<IAmazonCloudWatchLogs>();
        builder.Services.AddAWSService<IAmazonSecretsManager>();
        builder.Services.AddHttpClient<GitHubClient>(client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BuildSystemAgent/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });
    }
}

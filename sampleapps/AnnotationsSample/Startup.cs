// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.S3;
using AWS.AgentCore.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AnnotationsSample;

[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        // Aspire ServiceDefaults sets up OpenTelemetry (incl. AddAgentCoreInstrumentation),
        // health checks, service discovery, and HTTP resilience. AddAgentCore() does not need
        // EnableObservability when ServiceDefaults owns the OTel pipeline.
        builder.AddServiceDefaults();

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

        builder.Services.AddAWSService<IAmazonS3>();
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon;
using Amazon.BedrockAgentCore;
using ChatBotUI.Components;
using ChatBotUI.Models;
using ChatBotUI.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure AgentCore settings from appsettings.json
builder.Services.Configure<AgentCoreSettings>(settings =>
{
    builder.Configuration.GetSection("AgentCore").Bind(settings);

    // When running locally via Aspire, use a placeholder ARN
    var serviceEndpoint = builder.Configuration["AGENTCORE_SERVICE_ENDPOINT"];
    if (!string.IsNullOrEmpty(serviceEndpoint))
    {
        if (string.IsNullOrEmpty(settings.RuntimeArn) || settings.RuntimeArn.StartsWith("<"))
            settings.RuntimeArn = "local-agent";
        if (string.IsNullOrEmpty(settings.StreamingRuntimeArn) || settings.StreamingRuntimeArn.StartsWith("<"))
            settings.StreamingRuntimeArn = "local-agent";
    }
});

// Register the AWS SDK client.
// When running under Aspire with .WithReference(agent), the runtime emulator endpoint
// is injected as the AGENTCORE_SERVICE_ENDPOINT environment variable.
builder.Services.AddSingleton<IAmazonBedrockAgentCore>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var settings = config.GetSection("AgentCore").Get<AgentCoreSettings>()!;

    var serviceEndpoint = config["AGENTCORE_SERVICE_ENDPOINT"];

    if (!string.IsNullOrEmpty(serviceEndpoint))
    {
        // Aspire-injected — point the SDK at the local runtime emulator with anonymous credentials
        return new AmazonBedrockAgentCoreClient(
            new Amazon.Runtime.AnonymousAWSCredentials(),
            new AmazonBedrockAgentCoreConfig
            {
                ServiceURL = serviceEndpoint,
                AuthenticationRegion = settings.Region
            });
    }

    // Standard production — use real AWS credentials and region
    var region = RegionEndpoint.GetBySystemName(settings.Region);
    return new AmazonBedrockAgentCoreClient(region);
});

// Register services
builder.Services.AddSingleton<AgentCoreService>();
builder.Services.AddScoped<ChatSessionManager>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
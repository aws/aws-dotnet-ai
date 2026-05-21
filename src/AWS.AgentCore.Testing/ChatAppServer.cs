// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using Amazon.BedrockAgentCore;
using AWS.AgentCore.Testing.Components;
using AWS.AgentCore.Testing.Models;
using AWS.AgentCore.Testing.Services;
using Microsoft.Extensions.FileProviders;

namespace AWS.AgentCore.Testing;

/// <summary>
/// Creates an embedded Kestrel server hosting the Blazor Chat App UI.
/// The Chat App communicates with the Runtime Emulator via the AWS SDK
/// (<see cref="IAmazonBedrockAgentCore"/>) to invoke the agent, providing
/// a web-based interface for interactive testing of AgentCore agents.
/// Static assets are served from the <c>wwwroot</c> directory adjacent to the assembly,
/// using <c>EnvironmentName = "Production"</c> to bypass the static web assets manifest.
/// </summary>
internal static class ChatAppServer
{
    /// <summary>
    /// Creates and configures the Chat App web application.
    /// </summary>
    /// <param name="serviceEndpoint">
    /// The Runtime Emulator's HTTP endpoint URL (e.g., <c>http://localhost:12345</c>).
    /// Used as the AWS SDK's <c>ServiceURL</c> override so requests route to the emulator.
    /// </param>
    /// <param name="port">
    /// The TCP port to listen on. Defaults to 0 (OS-assigned), but typically a pre-allocated port
    /// from <see cref="Services.PortAllocator"/>.
    /// </param>
    /// <param name="streaming">
    /// Whether to configure the Chat App for SSE streaming mode. When true, agent responses
    /// are streamed incrementally to the UI.
    /// </param>
    /// <param name="loggerProvider">
    /// Optional logger provider to redirect all ASP.NET Core logs (requests, errors, etc.)
    /// to Aspire's <c>ResourceLoggerService</c> for dashboard visibility.
    /// </param>
    /// <returns>A configured but not yet started <see cref="WebApplication"/>.</returns>
    public static WebApplication Create(string serviceEndpoint, int port = 0, bool streaming = false, ILoggerProvider? loggerProvider = null)
    {
        var assemblyName = typeof(ChatAppServer).Assembly.GetName().Name!;
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var wwwrootFileProvider = new PhysicalFileProvider(wwwrootPath);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = assemblyName,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = wwwrootPath,
            EnvironmentName = "Production"
        });

        if (loggerProvider is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddHubOptions(options => options.MaximumReceiveMessageSize = null);

        builder.Services.AddSingleton<IFileProvider>(wwwrootFileProvider);

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        builder.Services.Configure<AgentCoreSettings>(settings =>
        {
            settings.RuntimeArn = "local-agent";
            settings.UseStreaming = streaming;
        });

        builder.Services.AddSingleton<IAmazonBedrockAgentCore>(_ =>
            new AmazonBedrockAgentCoreClient(
                new Amazon.Runtime.AnonymousAWSCredentials(),
                new AmazonBedrockAgentCoreConfig
                {
                    ServiceURL = serviceEndpoint,
                    AuthenticationRegion = "us-east-1"
                }));

        builder.Services.AddSingleton<AgentCoreService>();
        builder.Services.AddScoped<ChatSessionManager>();

        var app = builder.Build();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = wwwrootFileProvider
        });

        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        return app;
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using Amazon.BedrockAgentCore;
using AWS.AgentCore.Testing.Models;
using AWS.AgentCore.Testing.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace AWS.AgentCore.Testing;

/// <summary>
/// Creates an embedded Kestrel server hosting a vanilla HTML/CSS/JS Chat App UI
/// with minimal API endpoints. The Chat App communicates with the Runtime Emulator
/// via the AWS SDK (<see cref="IAmazonBedrockAgentCore"/>) to invoke the agent,
/// providing a web-based interface for interactive testing of AgentCore agents.
/// Static assets are served from the <c>wwwroot</c> directory adjacent to the assembly.
/// </summary>
public static class ChatAppServer
{
    /// <summary>
    /// Creates and configures the Chat App web application.
    /// </summary>
    /// <param name="serviceEndpoint">
    /// The Runtime Emulator's HTTP endpoint URL (e.g., <c>http://localhost:12345</c>).
    /// Used as the AWS SDK's <c>ServiceURL</c> override so requests route to the emulator.
    /// </param>
    /// <param name="port">
    /// The TCP port to listen on. Defaults to 0 (OS-assigned); the actual bound port
    /// can be read from <see cref="WebApplication.Urls"/> after startup.
    /// </param>
    /// <param name="streaming">
    /// Whether to configure the Chat App for SSE streaming mode. When true, agent responses
    /// are streamed incrementally to the UI.
    /// </param>
    /// <param name="agentName">
    /// Optional display name for the agent, used for payload config persistence.
    /// Defaults to <c>"default"</c>.
    /// </param>
    /// <param name="loggerProvider">
    /// Optional logger provider to redirect all ASP.NET Core logs (requests, errors, etc.)
    /// to Aspire's <c>ResourceLoggerService</c> for dashboard visibility.
    /// </param>
    /// <returns>A configured but not yet started <see cref="WebApplication"/>.</returns>
    public static WebApplication Create(string serviceEndpoint, int port = 0, bool streaming = false, string? agentName = null, ILoggerProvider? loggerProvider = null)
    {
        var assembly = typeof(ChatAppServer).Assembly;
        var embeddedFileProvider = new ManifestEmbeddedFileProvider(assembly, "wwwroot");
        var configStore = new PayloadConfigStore(agentName ?? "default");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = assembly.GetName().Name!,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = "Production"
        });

        if (loggerProvider is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(loggerProvider);
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        // Configure settings
        builder.Services.Configure<AgentCoreSettings>(settings =>
        {
            settings.RuntimeArn = "local-agent";
            settings.UseStreaming = streaming;
        });

        // Register AWS SDK client with anonymous credentials (local emulator)
        builder.Services.AddSingleton<IAmazonBedrockAgentCore>(_ =>
            new AmazonBedrockAgentCoreClient(
                new Amazon.Runtime.AnonymousAWSCredentials(),
                new AmazonBedrockAgentCoreConfig
                {
                    ServiceURL = serviceEndpoint,
                    AuthenticationRegion = "us-east-1"
                }));

        builder.Services.AddSingleton<AgentCoreService>();
        builder.Services.AddSingleton<ChatSessionManager>();
        builder.Services.AddSingleton(configStore);

        var app = builder.Build();

        // Serve static files from embedded resources
        app.UseStaticFiles(new StaticFileOptions { FileProvider = embeddedFileProvider });

        // API endpoints
        MapApiEndpoints(app);

        // SPA fallback — serve index.html from embedded resources
        app.MapFallback(async context =>
        {
            var indexFile = embeddedFileProvider.GetFileInfo("index.html");
            if (!indexFile.Exists)
            {
                context.Response.StatusCode = 404;
                return;
            }
            context.Response.ContentType = "text/html";
            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
        });

        return app;
    }

    private static void MapApiEndpoints(WebApplication app)
    {
        // POST /api/invoke - non-streaming invocation
        app.MapPost("/api/invoke", async (InvokeRequest req, AgentCoreService agentService, ChatSessionManager sessions) =>
        {
            var session = sessions.GetOrCreateSession(req.SessionId);
            sessions.AddMessage(session.Id, new ChatMessage { Role = ChatRole.User, Content = req.UserInput ?? "" });

            var assistantMsg = new ChatMessage { Role = ChatRole.Assistant, Content = "" };
            sessions.AddMessage(session.Id, assistantMsg);

            var response = await agentService.InvokeAgentAsync(req.Payload, session.Id);
            assistantMsg.Content = response;

            return Results.Ok(new { content = response, sessionId = session.Id });
        });

        // POST /api/invoke-stream - SSE streaming
        app.MapPost("/api/invoke-stream", async (InvokeRequest req, AgentCoreService agentService,
            ChatSessionManager sessions, HttpContext httpContext) =>
        {
            var session = sessions.GetOrCreateSession(req.SessionId);
            sessions.AddMessage(session.Id, new ChatMessage { Role = ChatRole.User, Content = req.UserInput ?? "" });

            var assistantMsg = new ChatMessage { Role = ChatRole.Assistant, Content = "" };
            sessions.AddMessage(session.Id, assistantMsg);

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.Headers["X-Session-Id"] = session.Id;

            await foreach (var chunk in agentService.InvokeAgentStreamingAsync(
                req.Payload, session.Id, cancellationToken: httpContext.RequestAborted))
            {
                assistantMsg.Content += chunk;
                await httpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { chunk })}\n\n");
                await httpContext.Response.Body.FlushAsync();
            }

            await httpContext.Response.WriteAsync("data: {\"done\":true}\n\n");
            await httpContext.Response.Body.FlushAsync();
        });

        // GET /api/sessions
        app.MapGet("/api/sessions", (ChatSessionManager sessions) =>
            Results.Ok(sessions.Sessions.Select(s => new { s.Id, s.Title, s.LastMessageAt })));

        // POST /api/sessions
        app.MapPost("/api/sessions", (ChatSessionManager sessions) =>
        {
            var session = sessions.CreateSession();
            return Results.Ok(new { session.Id, session.Title });
        });

        // DELETE /api/sessions/{id}
        app.MapDelete("/api/sessions/{id}", (string id, ChatSessionManager sessions) =>
        {
            sessions.DeleteSession(id);
            return Results.Ok();
        });

        // GET /api/sessions/{id}/messages
        app.MapGet("/api/sessions/{id}/messages", (string id, ChatSessionManager sessions) =>
        {
            var session = sessions.GetSession(id);
            if (session is null) return Results.NotFound();
            return Results.Ok(session.Messages.Select(m => new { m.Id, role = m.Role.ToString().ToLower(), m.Content, m.Timestamp }));
        });

        // GET /api/config
        app.MapGet("/api/config", (IOptions<AgentCoreSettings> settings, PayloadConfigStore configStore) =>
            Results.Ok(new { settings.Value.UseStreaming, agentName = configStore.AgentName }));

        // GET /api/payload-config — load persisted payload configuration
        app.MapGet("/api/payload-config", (PayloadConfigStore configStore) =>
        {
            var config = configStore.Load();
            if (config is null) return Results.NoContent();
            return Results.Content(config, "application/json");
        });

        // PUT /api/payload-config — save payload configuration
        app.MapPut("/api/payload-config", async (HttpContext httpContext, PayloadConfigStore configStore) =>
        {
            using var reader = new StreamReader(httpContext.Request.Body);
            var json = await reader.ReadToEndAsync();
            configStore.Save(json);
            return Results.Ok();
        });

        // DELETE /api/payload-config — reset to defaults
        app.MapDelete("/api/payload-config", (PayloadConfigStore configStore) =>
        {
            configStore.Delete();
            return Results.Ok();
        });
    }

}

internal record InvokeRequest(string Payload, string? SessionId, string? UserInput);

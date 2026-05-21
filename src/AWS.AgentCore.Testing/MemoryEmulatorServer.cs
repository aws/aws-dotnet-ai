// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AWS.AgentCore.Testing.Emulators.Memory;
using AWS.AgentCore.Testing.Emulators.Memory.Models;
using Microsoft.AspNetCore.Mvc;

namespace AWS.AgentCore.Testing;

/// <summary>
/// Creates an embedded Kestrel server that emulates the AgentCore Memory service.
/// Provides in-memory storage for conversation events, enabling the
/// <c>AgentCoreMemoryProvider</c> in the agent app to load and save chat history
/// without connecting to AWS. The API surface matches the wire format of the
/// <c>IAmazonBedrockAgentCore</c> SDK client for ListEvents and CreateEvent operations.
/// </summary>
internal static class MemoryEmulatorServer
{
    /// <summary>
    /// Creates and configures the Memory Emulator web application.
    /// </summary>
    /// <param name="port">
    /// The TCP port to listen on. Defaults to 0 (OS-assigned), but typically a pre-allocated port
    /// from <see cref="Services.PortAllocator"/>.
    /// </param>
    /// <returns>A configured but not yet started <see cref="WebApplication"/>.</returns>
    public static WebApplication Create(int port = 0)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MemoryEmulatorServer).Assembly.GetName().Name!,
            EnvironmentName = "Production"
        });

        // Prevent ASP.NET from trying to load hosting startup assemblies
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        builder.Services.AddSingleton<InMemoryEventStore>();

        var app = builder.Build();

        // CreateEvent: POST /memories/{memoryId}/events
        app.MapPost("/memories/{memoryId}/events", (
            string memoryId,
            [FromBody] CreateEventApiRequest body,
            InMemoryEventStore store) =>
        {
            return Results.Ok(store.CreateEvent(memoryId, body));
        });

        // ListEvents: POST /memories/{memoryId}/actor/{actorId}/sessions/{sessionId}
        // This matches the wire format sent by the AWS SDK client.
        app.MapPost("/memories/{memoryId}/actor/{actorId}/sessions/{sessionId}", async (
            string memoryId,
            string actorId,
            string sessionId,
            HttpContext httpContext,
            InMemoryEventStore store) =>
        {
            bool? includePayloads = null;
            int? maxResults = null;
            string? nextToken = null;

            try
            {
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(httpContext.Request.Body);
                if (doc.RootElement.TryGetProperty("includePayloads", out var ip))
                    includePayloads = ip.GetBoolean();
                if (doc.RootElement.TryGetProperty("maxResults", out var mr))
                    maxResults = mr.GetInt32();
                if (doc.RootElement.TryGetProperty("nextToken", out var nt))
                    nextToken = nt.GetString();
            }
            catch { /* empty or invalid body — use defaults */ }

            try
            {
                return Results.Ok(store.ListEvents(memoryId, actorId, sessionId, includePayloads, maxResults, nextToken));
            }
            catch (InvalidNextTokenException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // GET variant for direct testing and debugging
        app.MapGet("/memories/{memoryId}/actors/{actorId}/sessions/{sessionId}/events", (
            string memoryId,
            string actorId,
            string sessionId,
            [FromQuery] bool? includePayloads,
            [FromQuery] int? maxResults,
            [FromQuery] string? nextToken,
            InMemoryEventStore store) =>
        {
            try
            {
                return Results.Ok(store.ListEvents(memoryId, actorId, sessionId, includePayloads, maxResults, nextToken));
            }
            catch (InvalidNextTokenException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

        return app;
    }
}

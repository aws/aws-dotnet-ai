// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using AWS.AgentCore.Testing.Emulators.Runtime.Models;
using AWS.AgentCore.Testing.Emulators.Runtime;

namespace AWS.AgentCore.Testing;

/// <summary>
/// Creates an embedded Kestrel server that emulates the AgentCore Runtime service.
/// Exposes the SDK-compatible <c>POST /runtimes/{agentRuntimeArn}/invocations</c> endpoint,
/// allowing the AWS SDK (<c>IAmazonBedrockAgentCore</c>) to send requests that are forwarded
/// to the agent application's <c>POST /invocations</c> endpoint. Also provides developer-friendly
/// endpoints for direct prompt submission and session introspection.
///
/// The emulator passes the request payload through as-is (no JSON envelope wrapping),
/// adds the required <c>X-Amzn-Bedrock-AgentCore-Runtime-Session-Id</c> and
/// <c>X-Amzn-Bedrock-AgentCore-Runtime-Request-Id</c> headers, and supports both
/// standard JSON and SSE streaming response modes.
/// </summary>
internal static class RuntimeEmulatorServer
{
    /// <summary>
    /// Creates and configures the Runtime Emulator web application.
    /// </summary>
    /// <param name="agentEndpointUrl">
    /// The agent application's HTTP endpoint URL (e.g., <c>http://localhost:54321</c>).
    /// The emulator forwards invocation requests to <c>{agentEndpointUrl}/invocations</c>.
    /// </param>
    /// <param name="port">
    /// The TCP port to listen on. Defaults to 0 (OS-assigned), but typically a pre-allocated port
    /// from <see cref="Services.PortAllocator"/>.
    /// </param>
    /// <param name="loggerProvider">
    /// Optional logger provider to redirect all ASP.NET Core logs (requests, errors, etc.)
    /// to Aspire's <c>ResourceLoggerService</c> for dashboard visibility.
    /// </param>
    /// <returns>A configured but not yet started <see cref="WebApplication"/>.</returns>
    public static WebApplication Create(string agentEndpointUrl, int port = 0, ILoggerProvider? loggerProvider = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(RuntimeEmulatorServer).Assembly.GetName().Name!,
            EnvironmentName = "Production"
        });

        // Prevent ASP.NET from trying to load hosting startup assemblies
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, port);
        });

        // Register RuntimeEmulatorService with an HttpClient pointing at the agent
        builder.Services.AddHttpClient<RuntimeEmulatorService>(client =>
        {
            client.BaseAddress = new Uri(agentEndpointUrl);
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        builder.Services.AddLogging(logging =>
        {
            if (loggerProvider is not null)
            {
                logging.ClearProviders();
                logging.AddProvider(loggerProvider);
            }
        });

        var app = builder.Build();

        // SDK-compatible InvokeAgentRuntime endpoint
        // Wire format: POST /runtimes/{agentRuntimeArn}/invocations
        app.MapPost("/runtimes/{agentRuntimeArn}/invocations", async (HttpContext httpContext, RuntimeEmulatorService service) =>
        {
            var sessionId = httpContext.Request.Headers["X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"].FirstOrDefault();
            var acceptHeader = httpContext.Request.Headers.Accept.FirstOrDefault() ?? "application/json";
            var wantsStreaming = acceptHeader.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

            using var reader = new StreamReader(httpContext.Request.Body);
            var payload = await reader.ReadToEndAsync();

            var submission = new PromptSubmission(payload, sessionId);

            if (wantsStreaming)
            {
                await using var streamResult = await service.InvokeAgentStreamThroughAsync(submission, httpContext.RequestAborted);

                httpContext.Response.StatusCode = streamResult.StatusCode;
                httpContext.Response.Headers["X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"] = streamResult.SessionId;
                httpContext.Response.ContentType = streamResult.ContentType ?? "text/event-stream";

                await streamResult.ResponseStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
            else
            {
                var result = await service.InvokeAgentAsync(submission);

                httpContext.Response.StatusCode = result.StatusCode;
                httpContext.Response.Headers["X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"] = result.SessionId;
                httpContext.Response.ContentType = "application/json";
                await httpContext.Response.WriteAsync(result.Response);
            }
        });

        // Developer-friendly prompt endpoint (bypasses SDK wire format)
        app.MapPost("/api/prompt", async (PromptSubmission submission, RuntimeEmulatorService service) =>
        {
            var result = await service.InvokeAgentAsync(submission);
            return Results.Ok(result);
        });

        // Session introspection endpoint
        app.MapGet("/api/sessions", (RuntimeEmulatorService service) =>
        {
            return Results.Ok(service.GetActiveSessions());
        });

        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

        // Diagnostic fallback for unmatched routes
        app.MapFallback(async context =>
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RuntimeEmulator.Fallback");
            logger.LogWarning("No route matched: {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync($"No route matched: {context.Request.Method} {context.Request.Path}");
        });

        return app;
    }
}

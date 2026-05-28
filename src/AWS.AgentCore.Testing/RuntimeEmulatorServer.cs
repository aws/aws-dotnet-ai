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
public static class RuntimeEmulatorServer
{
    /// <summary>
    /// Creates and configures the Runtime Emulator web application.
    /// </summary>
    /// <param name="agentEndpointUrl">
    /// The agent application's HTTP endpoint URL (e.g., <c>http://localhost:54321</c>).
    /// The emulator forwards invocation requests to <c>{agentEndpointUrl}/invocations</c>.
    /// </param>
    /// <param name="port">
    /// The TCP port to listen on. Defaults to 0 (OS-assigned); the actual bound port
    /// can be read from <see cref="WebApplication.Urls"/> after startup.
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

        app.MapGet("/", (HttpContext httpContext) =>
        {
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            return httpContext.Response.WriteAsync(InfoPageHtml);
        });

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

    private const string InfoPageHtml = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>AWS Bedrock AgentCore — Runtime Emulator</title>
            <style>
                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f8f9fa; color: #1a1a2e; min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 2rem; }
                .card { background: #fff; border-radius: 12px; box-shadow: 0 2px 12px rgba(0,0,0,0.08); max-width: 700px; width: 100%; padding: 2.5rem; }
                h1 { font-size: 1.5rem; margin-bottom: 0.5rem; }
                .subtitle { color: #6c757d; margin-bottom: 1.5rem; line-height: 1.5; }
                h2 { font-size: 1.1rem; margin-bottom: 0.75rem; color: #495057; }
                .code-block { background: #f8f9fa; border: 1px solid #e9ecef; border-radius: 8px; padding: 1rem; font-family: 'SF Mono', Menlo, monospace; font-size: 0.82rem; line-height: 1.6; overflow-x: auto; margin-bottom: 1.5rem; }
                .code-block .comment { color: #6c757d; }
                .code-block .keyword { color: #0d6efd; }
                .code-block .string { color: #198754; }
                .note { background: #fff3cd; border-radius: 6px; padding: 0.75rem 1rem; font-size: 0.85rem; color: #664d03; line-height: 1.5; }
            </style>
        </head>
        <body>
            <div class="card">
                <h1>AWS Bedrock AgentCore — Runtime Emulator</h1>
                <p class="subtitle">This local emulator stands in for the AWS Bedrock AgentCore Runtime service. Point your <code>AmazonBedrockAgentCoreClient</code> at this URL to invoke your agent locally without deploying to AWS.</p>
                <h2>Usage</h2>
                <div class="code-block">
                    <span class="keyword">var</span> client = <span class="keyword">new</span> AmazonBedrockAgentCoreClient(<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;<span class="keyword">new</span> AnonymousAWSCredentials(),<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;<span class="keyword">new</span> AmazonBedrockAgentCoreConfig<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;{<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;ServiceURL = <span class="string">"<span id="emulator-url"></span>"</span><br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;});<br/><br/>
                    <span class="comment">// Invoke your agent the same way you would in production</span><br/>
                    <span class="keyword">var</span> response = <span class="keyword">await</span> client.InvokeAgentRuntimeAsync(<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;<span class="keyword">new</span> InvokeAgentRuntimeRequest<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;{<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;AgentRuntimeArn = <span class="string">"local-agent"</span>,<br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;Payload = <span class="string">"{ \"prompt\": \"Hello\" }"</span><br/>
                    &nbsp;&nbsp;&nbsp;&nbsp;});
                </div>
                <div class="note">
                    <strong>No AWS credentials required.</strong> The emulator accepts anonymous credentials and does not communicate with AWS. Your agent runs entirely on your local machine.
                </div>
            </div>
            <script>document.getElementById('emulator-url').textContent = window.location.origin;</script>
        </body>
        </html>
        """;
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.AgentCore.Extensions;

/// <summary>
/// Extension methods for mapping AgentCore endpoints on <see cref="IEndpointRouteBuilder"/>.
/// </summary>
public static class AgentCoreEndpointExtensions
{
    /// <summary>
    /// Maps the required AgentCore Runtime endpoints onto the application's route table.
    /// This is the main entry point for handling agent invocations from the AgentCore Runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The AgentCore Runtime communicates with your application using a simple HTTP-based service contract.
    /// This method registers two endpoints that satisfy that contract:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <term><c>POST /invocations</c></term>
    ///     <description>
    ///     Called by the AgentCore Runtime to invoke your agent. The request body is deserialized as
    ///     <typeparamref name="TRequest"/> and passed to the <paramref name="handler"/> along with the
    ///     registered <see cref="IChatClient"/> and a <see cref="CancellationToken"/>. The handler's
    ///     string return value is wrapped in a JSON response with <c>message</c> and <c>timestamp</c> fields.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><c>GET /ping</c></term>
    ///     <description>
    ///     A health-check endpoint used by the AgentCore Runtime to verify the application is running.
    ///     Returns <c>{"status":"healthy"}</c>.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// Pair this method with <see cref="AgentCoreBuilderExtensions.AddAgentCore"/> which registers
    /// the required services (<see cref="IChatClient"/>, <see cref="AgentCoreOptions"/>) and configures
    /// the listening port.
    /// </para>
    /// <para>
    /// See <see href="https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/runtime-service-contract.html">AgentCore Runtime service contract</see>
    /// for the full specification of the endpoints and expected behavior.
    /// </para>
    /// </remarks>
    /// <typeparam name="TRequest">
    /// The type to deserialize the <c>/invocations</c> request body into. This is typically a simple
    /// record or class with a <c>Prompt</c> property (e.g., <c>record PromptRequest(string? Prompt)</c>).
    /// </typeparam>
    /// <param name="app">The endpoint route builder to map the endpoints on.</param>
    /// <param name="handler">
    /// An asynchronous callback that processes each invocation. Receives the deserialized request body,
    /// the <see cref="IChatClient"/> resolved from dependency injection, and a <see cref="CancellationToken"/>.
    /// Return the agent's response as a plain string.
    /// </param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// app.MapAgentCore&lt;PromptRequest&gt;(async (request, chatClient, ct) =>
    /// {
    ///     var agent = chatClient.AsAIAgent();
    ///     var response = await agent.RunAsync(request.Prompt, ct);
    ///     return response.ToString();
    /// });
    /// </code>
    /// </example>
    public static IEndpointRouteBuilder MapAgentCore<TRequest>(
        this IEndpointRouteBuilder app,
        Func<TRequest, IChatClient, CancellationToken, Task<string>> handler)
    {
        app.MapPost("/invocations", async (HttpContext httpContext) =>
        {
            var request = await httpContext.Request.ReadFromJsonAsync<TRequest>(httpContext.RequestAborted);
            if (request is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { error = "Invalid request body." });
                return;
            }

            var chatClient = httpContext.RequestServices.GetRequiredService<IChatClient>();
            var result = await handler(request, chatClient, httpContext.RequestAborted);

            await httpContext.Response.WriteAsJsonAsync(new { message = result, timestamp = DateTime.UtcNow });
        });

        app.MapGet("/ping", () => Results.Ok(new { status = "healthy" }));

        return app;
    }
}

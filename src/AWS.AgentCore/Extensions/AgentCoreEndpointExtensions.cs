// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.AgentCore.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
    ///     <typeparamref name="TRequest"/> and passed to the <paramref name="handler"/>. Additional
    ///     parameters on the handler are resolved automatically using Minimal API-style binding:
    ///     <see cref="AgentCoreRuntimeContext"/> from AgentCore headers, <see cref="CancellationToken"/>
    ///     from the request, <see cref="HttpContext"/> directly, and any other type from the DI container.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term><c>GET /ping</c></term>
    ///     <description>
    ///     A health-check endpoint used by the AgentCore Runtime to verify the application is running.
    ///     Returns <c>{"status":"Healthy","time_of_last_update":&lt;unix_timestamp&gt;}</c> by default.
    ///     Supply a custom <paramref name="pingHandler"/> to override this behavior.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// The handler's return type determines the response format:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>Task&lt;string&gt;</c> or <c>string</c> — JSON response: <c>{"message":"...","timestamp":"..."}</c>
    ///   </description></item>
    ///   <item><description>
    ///     <c>IAsyncEnumerable&lt;string&gt;</c> — SSE streaming: each chunk sent as
    ///     <c>data: {"chunk":"..."}\n\n</c>, followed by a final
    ///     <c>data: {"message":"...","done":true}\n\n</c>
    ///   </description></item>
    /// </list>
    /// <para>
    /// The <paramref name="handler"/> delegate uses Minimal API-style parameter binding.
    /// Each parameter is resolved automatically based on its type:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><typeparamref name="TRequest"/> — deserialized from the JSON request body.</description></item>
    ///   <item><description><see cref="AgentCoreRuntimeContext"/> — populated from AgentCore HTTP headers.</description></item>
    ///   <item><description><see cref="CancellationToken"/> — the request's cancellation token.</description></item>
    ///   <item><description><see cref="HttpContext"/> — the raw HTTP context.</description></item>
    ///   <item><description>Any other type — resolved from the DI container via <c>RequestServices</c>.</description></item>
    /// </list>
    /// <para>
    /// Parameters can appear in any order and are all optional — include only what you need.
    /// </para>
    /// <para>
    /// Pair this method with <see cref="AgentCoreBuilderExtensions.AddAgentCore"/> which registers
    /// the required services (<see cref="AgentCoreOptions"/>) and configures the listening port.
    /// </para>
    /// <para>
    /// See <see href="https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/runtime-http-protocol-contract.html">AgentCore Runtime service contract</see>
    /// for the full specification of the endpoints and expected behavior.
    /// </para>
    /// </remarks>
    /// <typeparam name="TRequest">
    /// The type to deserialize the request body into (e.g., <c>record PromptRequest(string? Prompt)</c>).
    /// </typeparam>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="handler">
    /// An asynchronous handler delegate. Return <see cref="Task{String}"/> for a JSON response,
    /// or <see cref="IAsyncEnumerable{String}"/> for SSE streaming.
    /// Parameters are bound from the request body, AgentCore headers, DI container, or the HTTP context.
    /// </param>
    /// <param name="pingHandler">
    /// An optional delegate for the <c>GET /ping</c> health-check endpoint. When <c>null</c>
    /// (the default), a built-in handler returns <c>{"status":"Healthy","time_of_last_update":...}</c>.
    /// When provided, the delegate uses the same Minimal API-style DI binding as <paramref name="handler"/>
    /// (except <typeparamref name="TRequest"/> and <see cref="AgentCoreRuntimeContext"/> are
    /// not available). The return value is serialized as JSON.
    /// </param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> for further chaining.</returns>
    /// <example>
    /// <code>
    /// // Non-streaming — returns JSON
    /// app.MapAgentCore&lt;PromptRequest&gt;(async (PromptRequest request, IChatClient chatClient, CancellationToken ct) =>
    /// {
    ///     var response = await chatClient.GetResponseAsync(request.Prompt, ct);
    ///     return response.Text;
    /// });
    ///
    /// // Streaming — returns SSE
    /// app.MapAgentCore&lt;PromptRequest&gt;(async IAsyncEnumerable&lt;string&gt;
    ///     (PromptRequest request, IChatClient chatClient, [EnumeratorCancellation] CancellationToken ct) =>
    /// {
    ///     await foreach (var chunk in chatClient.StreamAsync(request.Prompt, ct))
    ///         yield return chunk;
    /// });
    ///
    /// // Custom ping handler
    /// app.MapAgentCore&lt;PromptRequest&gt;(
    ///     handler: async (PromptRequest request) => "response",
    ///     pingHandler: () => new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
    /// </code>
    /// </example>
    public static IEndpointRouteBuilder MapAgentCore<TRequest>(
        this IEndpointRouteBuilder app,
        Delegate handler,
        Delegate? pingHandler = null)
    {
        var bindingPlan = ParameterBindingPlan.Create<TRequest>(handler);

        app.MapPost("/invocations", async (HttpContext httpContext) =>
        {
            var request = await httpContext.Request.ReadFromJsonAsync<TRequest>(httpContext.RequestAborted);
            if (request is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { error = "Invalid request body." });
                return;
            }

            var args = bindingPlan.ResolveArguments(request, httpContext);

            if (bindingPlan.IsStreaming)
            {
                await StreamingResponseWriter.WriteStreamingResponseAsync(httpContext, handler, args);
            }
            else
            {
                var result = await bindingPlan.InvokeAsync(handler, args);
                await httpContext.Response.WriteAsJsonAsync(new { message = result, timestamp = DateTime.UtcNow });
            }
        });

        if (pingHandler is not null)
        {
            var pingBindingPlan = PingBindingPlan.Create(pingHandler);

            app.MapGet("/ping", async (HttpContext httpContext) =>
            {
                var args = pingBindingPlan.ResolveArguments(httpContext);
                var result = await pingBindingPlan.InvokeAsync(pingHandler, args);
                await httpContext.Response.WriteAsJsonAsync(result);
            });
        }
        else
        {
            app.MapGet("/ping", () => Results.Ok(new { status = "Healthy", time_of_last_update = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));
        }

        return app;
    }
}

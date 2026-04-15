// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
                await WriteStreamingResponse(httpContext, handler, args);
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

    /// <summary>
    /// Writes an SSE streaming response by iterating the handler's <see cref="IAsyncEnumerable{String}"/>
    /// result. Each chunk is sent as <c>data: {"chunk":"..."}\n\n</c>. A final event with the full
    /// accumulated message and <c>"done":true</c> is sent after the stream completes.
    /// If the handler throws before streaming starts, a JSON 500 error is returned instead.
    /// Errors after headers are written are sent as <c>data: {"error":"..."}\n\n</c>.
    /// </summary>
    private static async Task WriteStreamingResponse(HttpContext httpContext, Delegate handler, object?[] args)
    {
        IAsyncEnumerable<string> stream;

        try
        {
            var result = handler.DynamicInvoke(args);
            if (result is not IAsyncEnumerable<string> asyncStream)
            {
                throw new InvalidOperationException(
                    $"Streaming handler must return IAsyncEnumerable<string>, but returned {result?.GetType().Name ?? "null"}.");
            }
            stream = asyncStream;
        }
        catch (Exception ex)
        {
            // Handler threw before yielding — response hasn't started, return JSON error
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(new { error = ex.InnerException?.Message ?? ex.Message });
            return;
        }

        var fullMessage = new System.Text.StringBuilder();
        var headersWritten = false;

        try
        {
            await foreach (var chunk in stream.WithCancellation(httpContext.RequestAborted))
            {
                if (string.IsNullOrEmpty(chunk)) continue;

                if (!headersWritten)
                {
                    httpContext.Response.ContentType = "text/event-stream";
                    httpContext.Response.Headers.CacheControl = "no-cache";
                    httpContext.Response.Headers.Connection = "keep-alive";
                    headersWritten = true;
                }

                fullMessage.Append(chunk);
                var chunkJson = JsonSerializer.Serialize(new { chunk });
                await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }

            if (!headersWritten)
            {
                // Stream completed without yielding any chunks — return empty JSON response
                await httpContext.Response.WriteAsJsonAsync(new { message = string.Empty, timestamp = DateTime.UtcNow });
                return;
            }

            var doneJson = JsonSerializer.Serialize(new { message = fullMessage.ToString(), timestamp = DateTime.UtcNow, done = true });
            await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to write
        }
        catch (Exception ex)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsJsonAsync(new { error = ex.Message });
            }
            else
            {
                var errorJson = JsonSerializer.Serialize(new { error = ex.Message });
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n");
                await httpContext.Response.Body.FlushAsync();
            }
        }
    }

    /// <summary>
    /// Pre-computed binding plan that maps each parameter of a user-supplied handler delegate
    /// to a <see cref="ParameterSource"/>. Also detects whether the handler returns
    /// <see cref="IAsyncEnumerable{String}"/> (streaming) or <see cref="Task{String}"/> (non-streaming).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binding plan is created once at startup (inside <see cref="MapAgentCore{TRequest}"/>)
    /// by reflecting over the handler's <see cref="MethodInfo"/>. For every parameter, the plan
    /// records <em>where</em> the argument value should come from at request time. This avoids
    /// repeated reflection on the hot path.
    /// </para>
    /// <para><b>Binding rules (evaluated in order):</b></para>
    /// <list type="number">
    ///   <item><description>
    ///     If the parameter type matches <typeparamref name="TRequest"/> →
    ///     <see cref="ParameterSource.Request"/>: the deserialized JSON request body.
    ///   </description></item>
    ///   <item><description>
    ///     If the parameter type is <see cref="AgentCoreRuntimeContext"/> →
    ///     <see cref="ParameterSource.RuntimeContext"/>: constructed from AgentCore HTTP headers.
    ///   </description></item>
    ///   <item><description>
    ///     If the parameter type is <see cref="CancellationToken"/> →
    ///     <see cref="ParameterSource.CancellationToken"/>: bound to <see cref="HttpContext.RequestAborted"/>.
    ///   </description></item>
    ///   <item><description>
    ///     If the parameter type is <see cref="HttpContext"/> →
    ///     <see cref="ParameterSource.HttpContext"/>: the raw HTTP context.
    ///   </description></item>
    ///   <item><description>
    ///     Otherwise → <see cref="ParameterSource.Service"/>: resolved from the DI container.
    ///   </description></item>
    /// </list>
    /// </remarks>
    internal sealed class ParameterBindingPlan
    {
        /// <summary>
        /// Parallel arrays: <c>_sources[i]</c> tells us <em>where</em> to get the value for
        /// <c>_parameters[i]</c>. Both arrays have the same length — one entry per handler parameter.
        /// </summary>
        private readonly ParameterSource[] _sources;
        private readonly ParameterInfo[] _parameters;

        /// <summary>
        /// <c>true</c> when the handler returns <see cref="IAsyncEnumerable{String}"/>,
        /// indicating the endpoint should use SSE streaming. Determined once at startup.
        /// </summary>
        internal bool IsStreaming { get; }

        private ParameterBindingPlan(ParameterInfo[] parameters, ParameterSource[] sources, bool isStreaming)
        {
            _parameters = parameters;
            _sources = sources;
            IsStreaming = isStreaming;
        }

        /// <summary>
        /// Inspects the handler delegate's signature and builds a binding plan.
        /// Called once at map-time (startup), not per-request.
        /// </summary>
        /// <typeparam name="TRequest">
        /// The request body type. Any parameter whose type matches this is bound from the
        /// deserialized JSON body.
        /// </typeparam>
        /// <param name="handler">The user-supplied handler delegate to analyze.</param>
        /// <returns>A reusable binding plan for the handler's parameters.</returns>
        internal static ParameterBindingPlan Create<TRequest>(Delegate handler)
        {
            var method = handler.Method;
            var parameters = method.GetParameters();
            var sources = new ParameterSource[parameters.Length];
            var requestType = typeof(TRequest);

            for (var i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (paramType == requestType)
                    sources[i] = ParameterSource.Request;
                else if (paramType == typeof(AgentCoreRuntimeContext))
                    sources[i] = ParameterSource.RuntimeContext;
                else if (paramType == typeof(CancellationToken))
                    sources[i] = ParameterSource.CancellationToken;
                else if (paramType == typeof(HttpContext))
                    sources[i] = ParameterSource.HttpContext;
                else
                    sources[i] = ParameterSource.Service;
            }

            var isStreaming = typeof(IAsyncEnumerable<string>).IsAssignableFrom(method.ReturnType);

            return new ParameterBindingPlan(parameters, sources, isStreaming);
        }

        /// <summary>
        /// Materializes the argument array for a single request by reading from the appropriate
        /// source for each parameter.
        /// </summary>
        /// <typeparam name="TRequest">The request body type.</typeparam>
        /// <param name="request">The deserialized request body.</param>
        /// <param name="httpContext">The current HTTP context (provides headers, DI, cancellation).</param>
        /// <returns>
        /// An <c>object?[]</c> aligned with the handler's parameter list, ready for
        /// <see cref="InvokeAsync"/> or streaming invocation.
        /// </returns>
        internal object?[] ResolveArguments<TRequest>(TRequest request, HttpContext httpContext)
        {
            var args = new object?[_sources.Length];

            for (var i = 0; i < _sources.Length; i++)
            {
                args[i] = _sources[i] switch
                {
                    ParameterSource.Request => request,
                    ParameterSource.RuntimeContext => AgentCoreRuntimeContext.FromHttpContext(httpContext),
                    ParameterSource.CancellationToken => httpContext.RequestAborted,
                    ParameterSource.HttpContext => httpContext,
                    ParameterSource.Service => httpContext.RequestServices.GetRequiredService(_parameters[i].ParameterType),
                    _ => throw new InvalidOperationException($"Unknown parameter source for '{_parameters[i].Name}'.")
                };
            }

            return args;
        }

        /// <summary>
        /// Invokes a non-streaming handler and unwraps the result.
        /// Supports <see cref="Task{String}"/> and plain <see cref="string"/> returns.
        /// </summary>
        /// <param name="handler">The user-supplied handler delegate.</param>
        /// <param name="args">
        /// The argument array produced by <see cref="ResolveArguments{TRequest}"/>.
        /// </param>
        /// <returns>The handler's string result.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the handler returns a type other than <see cref="Task{String}"/>,
        /// <see cref="string"/>, or <see cref="IAsyncEnumerable{String}"/>.
        /// </exception>
        internal async Task<string> InvokeAsync(Delegate handler, object?[] args)
        {
            var result = handler.DynamicInvoke(args);

            return result switch
            {
                Task<string> taskString => await taskString,
                Task<object> taskObj => (await taskObj)?.ToString() ?? string.Empty,
                string s => s,
                _ => throw new InvalidOperationException(
                    $"Handler must return Task<string> or IAsyncEnumerable<string>, but returned {result?.GetType().Name ?? "null"}.")
            };
        }
    }

    /// <summary>
    /// Pre-computed binding plan for the <c>/ping</c> health-check handler.
    /// Supports only <see cref="CancellationToken"/>, <see cref="HttpContext"/>, and DI services.
    /// <see cref="AgentCoreRuntimeContext"/> and <c>TRequest</c> are not available for ping handlers
    /// because the health-check endpoint has no request body or AgentCore headers.
    /// </summary>
    internal sealed class PingBindingPlan
    {
        private readonly ParameterSource[] _sources;
        private readonly ParameterInfo[] _parameters;

        private PingBindingPlan(ParameterInfo[] parameters, ParameterSource[] sources)
        {
            _parameters = parameters;
            _sources = sources;
        }

        /// <summary>
        /// Inspects the ping handler delegate's signature and builds a binding plan.
        /// Only <see cref="CancellationToken"/>, <see cref="HttpContext"/>, and DI service
        /// parameters are supported.
        /// </summary>
        /// <param name="handler">The user-supplied ping handler delegate.</param>
        /// <returns>A reusable binding plan for the ping handler's parameters.</returns>
        internal static PingBindingPlan Create(Delegate handler)
        {
            var method = handler.Method;
            var parameters = method.GetParameters();
            var sources = new ParameterSource[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (paramType == typeof(CancellationToken))
                    sources[i] = ParameterSource.CancellationToken;
                else if (paramType == typeof(HttpContext))
                    sources[i] = ParameterSource.HttpContext;
                else
                    sources[i] = ParameterSource.Service;
            }

            return new PingBindingPlan(parameters, sources);
        }

        /// <summary>
        /// Materializes the argument array for a ping request.
        /// </summary>
        /// <param name="httpContext">The current HTTP context.</param>
        /// <returns>An <c>object?[]</c> ready for delegate invocation.</returns>
        internal object?[] ResolveArguments(HttpContext httpContext)
        {
            var args = new object?[_sources.Length];

            for (var i = 0; i < _sources.Length; i++)
            {
                args[i] = _sources[i] switch
                {
                    ParameterSource.CancellationToken => httpContext.RequestAborted,
                    ParameterSource.HttpContext => httpContext,
                    ParameterSource.Service => httpContext.RequestServices.GetRequiredService(_parameters[i].ParameterType),
                    _ => throw new InvalidOperationException($"Unsupported parameter source for ping handler: '{_parameters[i].Name}'.")
                };
            }

            return args;
        }

        /// <summary>
        /// Invokes the ping handler and returns the result as an object for JSON serialization.
        /// Supports <see cref="Task{T}"/>, <see cref="Task"/>, and synchronous return types.
        /// </summary>
        internal async Task<object?> InvokeAsync(Delegate handler, object?[] args)
        {
            var result = handler.DynamicInvoke(args);

            return result switch
            {
                Task<object> taskObj => await taskObj,
                Task task => await task.ContinueWith(_ => (object?)null, TaskScheduler.Default),
                _ => result
            };
        }
    }

    /// <summary>
    /// Identifies where a handler parameter's value comes from at request time.
    /// </summary>
    internal enum ParameterSource
    {
        /// <summary>Deserialized from the JSON request body as <c>TRequest</c>.</summary>
        Request,

        /// <summary>Populated from AgentCore HTTP headers.</summary>
        RuntimeContext,

        /// <summary>Bound to <see cref="HttpContext.RequestAborted"/>.</summary>
        CancellationToken,

        /// <summary>The raw <see cref="HttpContext"/> for the current request.</summary>
        HttpContext,

        /// <summary>Resolved from the DI container via <see cref="HttpContext.RequestServices"/>.</summary>
        Service
    }
}

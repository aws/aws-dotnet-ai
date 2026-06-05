// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// Pre-computed binding plan that maps each parameter of a user-supplied handler delegate
/// to a <see cref="ParameterSource"/>. Also detects whether the handler returns
/// <see cref="IAsyncEnumerable{String}"/> (streaming) or <see cref="Task{String}"/> (non-streaming).
/// </summary>
/// <remarks>
/// <para>
/// The binding plan is created once at startup by reflecting over the handler's <see cref="MethodInfo"/>.
/// For every parameter, the plan records <em>where</em> the argument value should come from at request time.
/// This avoids repeated reflection on the hot path.
/// </para>
/// <para><b>Binding rules (evaluated in order):</b></para>
/// <list type="number">
///   <item><description><c>TRequest</c> → <see cref="ParameterSource.Request"/>: the deserialized JSON request body.</description></item>
///   <item><description><see cref="AgentCoreRuntimeContext"/> → <see cref="ParameterSource.RuntimeContext"/>: constructed from AgentCore HTTP headers.</description></item>
///   <item><description><see cref="CancellationToken"/> → <see cref="ParameterSource.CancellationToken"/>: bound to <see cref="HttpContext.RequestAborted"/>.</description></item>
///   <item><description><see cref="HttpContext"/> → <see cref="ParameterSource.HttpContext"/>: the raw HTTP context.</description></item>
///   <item><description>Otherwise → <see cref="ParameterSource.Service"/>: resolved from the DI container.</description></item>
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

        // Set the ambient AsyncLocal so downstream code (e.g. AgentCoreMemoryProvider)
        // can access the runtime context without manual StateBag population.
        var runtimeContext = args.OfType<AgentCoreRuntimeContext>().FirstOrDefault()
            ?? AgentCoreRuntimeContext.FromHttpContext(httpContext);
        AgentCoreRuntimeContextProvider.CurrentContext = runtimeContext;

        return args;
    }

    /// <summary>
    /// Invokes a non-streaming handler and unwraps the result.
    /// Supports <see cref="Task{String}"/> and plain <see cref="string"/> returns.
    /// </summary>
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

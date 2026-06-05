// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.AgentCore.Hosting.Internal;

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

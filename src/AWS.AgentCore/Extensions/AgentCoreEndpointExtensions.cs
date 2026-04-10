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
    /// Maps the AgentCore <c>/invocations</c> and <c>/ping</c> endpoints.
    /// The invocation handler receives the deserialized request body of type <typeparamref name="TRequest"/>.
    /// </summary>
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

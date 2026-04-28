// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace AWS.AgentCore.Internal;

/// <summary>
/// Writes SSE streaming responses for AgentCore <c>/invocations</c> endpoints.
/// </summary>
internal static class StreamingResponseWriter
{
    /// <summary>
    /// Writes an SSE streaming response by iterating the handler's <see cref="IAsyncEnumerable{String}"/>
    /// result. Each chunk is sent as <c>data: {"chunk":"..."}\n\n</c>. A final event with the full
    /// accumulated message and <c>"done":true</c> is sent after the stream completes.
    /// If the handler throws before streaming starts, a JSON 500 error is returned instead.
    /// Errors after headers are written are sent as <c>data: {"error":"..."}\n\n</c>.
    /// </summary>
    internal static async Task WriteStreamingResponseAsync(HttpContext httpContext, Delegate handler, object?[] args)
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
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(
                new SseErrorResponse(ex.InnerException?.Message ?? ex.Message),
                AgentCoreJsonContext.Default.SseErrorResponse);
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
                var chunkJson = JsonSerializer.Serialize(new SseChunkResponse(chunk), AgentCoreJsonContext.Default.SseChunkResponse);
                await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }

            if (!headersWritten)
            {
                await httpContext.Response.WriteAsJsonAsync(
                    new JsonEmptyMessageResponse(string.Empty, DateTime.UtcNow),
                    AgentCoreJsonContext.Default.JsonEmptyMessageResponse);
                return;
            }

            var doneJson = JsonSerializer.Serialize(
                new SseDoneResponse(fullMessage.ToString(), DateTime.UtcNow, true),
                AgentCoreJsonContext.Default.SseDoneResponse);
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
                await httpContext.Response.WriteAsJsonAsync(
                    new SseErrorResponse(ex.Message),
                    AgentCoreJsonContext.Default.SseErrorResponse);
            }
            else
            {
                var errorJson = JsonSerializer.Serialize(
                    new SseErrorResponse(ex.Message),
                    AgentCoreJsonContext.Default.SseErrorResponse);
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n");
                await httpContext.Response.Body.FlushAsync();
            }
        }
    }

    /// <summary>
    /// Writes an SSE streaming response from a pre-resolved <see cref="IAsyncEnumerable{String}"/>.
    /// NativeAOT-compatible — no reflection or DynamicInvoke.
    /// </summary>
    internal static async Task WriteStreamingResponseAsync(HttpContext httpContext, IAsyncEnumerable<string> stream)
    {
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
                var chunkJson = JsonSerializer.Serialize(new SseChunkResponse(chunk), AgentCoreJsonContext.Default.SseChunkResponse);
                await httpContext.Response.WriteAsync($"data: {chunkJson}\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }

            if (!headersWritten)
            {
                await httpContext.Response.WriteAsJsonAsync(
                    new JsonEmptyMessageResponse(string.Empty, DateTime.UtcNow),
                    AgentCoreJsonContext.Default.JsonEmptyMessageResponse);
                return;
            }

            var doneJson = JsonSerializer.Serialize(
                new SseDoneResponse(fullMessage.ToString(), DateTime.UtcNow, true),
                AgentCoreJsonContext.Default.SseDoneResponse);
            await httpContext.Response.WriteAsync($"data: {doneJson}\n\n", httpContext.RequestAborted);
            await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        catch (Exception ex)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await httpContext.Response.WriteAsJsonAsync(
                    new SseErrorResponse(ex.Message),
                    AgentCoreJsonContext.Default.SseErrorResponse);
            }
            else
            {
                var errorJson = JsonSerializer.Serialize(
                    new SseErrorResponse(ex.Message),
                    AgentCoreJsonContext.Default.SseErrorResponse);
                await httpContext.Response.WriteAsync($"data: {errorJson}\n\n");
                await httpContext.Response.Body.FlushAsync();
            }
        }
    }
}

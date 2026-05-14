// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;

namespace AWS.AgentCore.IntegrationTests.Infrastructure;

/// <summary>
/// Invokes AgentCore Runtime agents and parses responses.
/// Supports both standard (JSON) and streaming (SSE) invocations.
/// </summary>
public sealed class AgentCoreInvoker : IDisposable
{
    private readonly AmazonBedrockAgentCoreClient _client;
    private readonly string _region;

    public AgentCoreInvoker(string region)
    {
        _region = region;
        _client = new AmazonBedrockAgentCoreClient(RegionEndpoint.GetBySystemName(region));
    }

    /// <summary>
    /// Invokes a non-streaming agent and returns the parsed message from the JSON response.
    /// </summary>
    public async Task<InvocationResult> InvokeAsync(string runtimeArn, string prompt, CancellationToken ct = default, string? sessionId = null)
    {
        var payload = JsonSerializer.Serialize(new { prompt });

        // Retry up to 3 times with delay to handle cold start / transient 500s
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var request = new InvokeAgentRuntimeRequest
                {
                    AgentRuntimeArn = runtimeArn,
                    Payload = new MemoryStream(Encoding.UTF8.GetBytes(payload)),
                    ContentType = "application/json",
                    Accept = "application/json",
                };

                if (!string.IsNullOrEmpty(sessionId))
                {
                    request.RuntimeSessionId = sessionId;
                }

                var response = await _client.InvokeAgentRuntimeAsync(request, ct);

                using var reader = new StreamReader(response.Response);
                var responseBody = await reader.ReadToEndAsync(ct);

                string? message = null;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("message", out var messageProp))
                        message = messageProp.GetString();
                }
                catch (JsonException)
                {
                    // Not JSON — use raw body
                }

                return new InvocationResult
                {
                    RawBody = responseBody,
                    Message = message ?? responseBody,
                    HttpStatusCode = (int)response.HttpStatusCode,
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < 3)
                {
                    Console.Error.WriteLine($"[AgentCore] Invocation attempt {attempt} failed: {ex.Message}. Retrying in 10s...");
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
        }

        // Dump CloudWatch logs immediately after final failure
        string cwLogs = "";
        try
        {
            await CloudWatchLogHelper.DumpRuntimeLogsAsync(runtimeArn, _region, ct);
            cwLogs = CloudWatchLogHelper.FlushLogBuffer();
        }
        catch { /* best effort */ }

        throw new InvalidOperationException(
            $"All invocation attempts failed for {runtimeArn}.{Environment.NewLine}" +
            $"CloudWatch logs:{Environment.NewLine}{cwLogs}",
            lastException);
    }

    /// <summary>
    /// Invokes a streaming agent and collects all SSE chunks into a result.
    /// </summary>
    public async Task<StreamingInvocationResult> InvokeStreamingAsync(
        string runtimeArn, string prompt, CancellationToken ct = default, string? sessionId = null)
    {
        var payload = JsonSerializer.Serialize(new { prompt });

        // Retry up to 3 times with delay to handle cold start / transient 500s
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var request = new InvokeAgentRuntimeRequest
                {
                    AgentRuntimeArn = runtimeArn,
                    Payload = new MemoryStream(Encoding.UTF8.GetBytes(payload)),
                    ContentType = "application/json",
                    Accept = "text/event-stream",
                };

                if (!string.IsNullOrEmpty(sessionId))
                {
                    request.RuntimeSessionId = sessionId;
                }

                var response = await _client.InvokeAgentRuntimeAsync(request, ct);

                var chunks = new List<string>();
                string? finalMessage = null;

                using var reader = new StreamReader(response.Response);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;
                    if (!line.StartsWith("data: ")) continue;

                    var json = line["data: ".Length..];
                    try
                    {
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean())
                        {
                            if (doc.RootElement.TryGetProperty("message", out var msgProp))
                                finalMessage = msgProp.GetString();
                            break;
                        }

                        if (doc.RootElement.TryGetProperty("chunk", out var chunkProp))
                        {
                            var chunk = chunkProp.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                                chunks.Add(chunk);
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed SSE events
                    }
                }

                return new StreamingInvocationResult
                {
                    Chunks = chunks,
                    FinalMessage = finalMessage ?? string.Concat(chunks),
                    HttpStatusCode = (int)response.HttpStatusCode,
                };
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < 3)
                {
                    Console.Error.WriteLine($"[AgentCore] Streaming invocation attempt {attempt} failed: {ex.Message}. Retrying in 10s...");
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
        }

        // Dump CloudWatch logs immediately after final failure
        string cwLogs = "";
        try
        {
            await CloudWatchLogHelper.DumpRuntimeLogsAsync(runtimeArn, _region, ct);
            cwLogs = CloudWatchLogHelper.FlushLogBuffer();
        }
        catch { /* best effort */ }

        throw new InvalidOperationException(
            $"All streaming invocation attempts failed for {runtimeArn}.{Environment.NewLine}" +
            $"CloudWatch logs:{Environment.NewLine}{cwLogs}",
            lastException);
    }

    public void Dispose() => _client.Dispose();
}

public class InvocationResult
{
    public string RawBody { get; set; } = "";
    public string Message { get; set; } = "";
    public int HttpStatusCode { get; set; }
}

public class StreamingInvocationResult
{
    public List<string> Chunks { get; set; } = new();
    public string FinalMessage { get; set; } = "";
    public int HttpStatusCode { get; set; }
}

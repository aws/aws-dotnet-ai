// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using ChatBotUI.Models;
using Microsoft.Extensions.Options;

namespace ChatBotUI.Services;

public class AgentCoreService
{
    private readonly AmazonBedrockAgentCoreClient _client;
    private readonly AgentCoreSettings _settings;
    private readonly ILogger<AgentCoreService> _logger;

    public AgentCoreService(IOptions<AgentCoreSettings> settings, ILogger<AgentCoreService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var region = RegionEndpoint.GetBySystemName(_settings.Region);
        _client = new AmazonBedrockAgentCoreClient(region);
    }

    /// <summary>
    /// Invokes the AgentCore Runtime agent and returns the full response.
    /// </summary>
    public async Task<string> InvokeAgentAsync(string prompt, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Invoking AgentCore Runtime: {Arn}", _settings.RuntimeArn);

        var payload = JsonSerializer.Serialize(new { prompt });
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        var request = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = _settings.RuntimeArn,
            Payload = new MemoryStream(payloadBytes),
            ContentType = "application/json",
            Accept = "application/json"
        };

        if (!string.IsNullOrEmpty(sessionId))
        {
            request.RuntimeSessionId = sessionId;
        }

        try
        {
            var response = await _client.InvokeAgentRuntimeAsync(request, cancellationToken);

            using var reader = new StreamReader(response.Response);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);

            _logger.LogInformation("Received response (length={Length})", responseBody.Length);

            // Try to parse the response JSON and extract the message
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("message", out var messageProp))
                {
                    return messageProp.GetString() ?? responseBody;
                }
            }
            catch (JsonException)
            {
                // If not JSON, return raw response
            }

            return responseBody;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking AgentCore Runtime");
            throw;
        }
    }

    /// <summary>
    /// Invokes the AgentCore Runtime streaming agent and yields response chunks as they arrive via SSE.
    /// </summary>
    public async IAsyncEnumerable<string> InvokeAgentStreamingAsync(
        string prompt, string? sessionId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var arn = _settings.StreamingRuntimeArn;
        if (string.IsNullOrEmpty(arn))
        {
            yield return "Error: StreamingRuntimeArn is not configured in appsettings.json";
            yield break;
        }

        _logger.LogInformation("Invoking streaming AgentCore Runtime: {Arn}", arn);

        var payload = JsonSerializer.Serialize(new { prompt });
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        var request = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = arn,
            Payload = new MemoryStream(payloadBytes),
            ContentType = "application/json",
            Accept = "text/event-stream",
        };

        if (!string.IsNullOrEmpty(sessionId))
        {
            request.RuntimeSessionId = sessionId;
        }

        InvokeAgentRuntimeResponse? response = null;
        string? invokeError = null;
        try
        {
            response = await _client.InvokeAgentRuntimeAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking streaming AgentCore Runtime");
            invokeError = $"Error: {ex.Message}";
        }

        if (invokeError is not null)
        {
            yield return invokeError;
            yield break;
        }

        using var reader = new StreamReader(response!.Response);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (!line.StartsWith("data: ")) continue;

            var json = line["data: ".Length..];
            var chunk = ParseSseChunk(json);

            if (chunk is null) break; // "done" event
            if (chunk.Length > 0) yield return chunk;
        }
    }

    /// <summary>
    /// Parses an SSE data payload. Returns the chunk text, empty string for skip, or null for "done".
    /// </summary>
    private string? ParseSseChunk(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean())
                return null;

            if (doc.RootElement.TryGetProperty("chunk", out var chunkProp))
                return chunkProp.GetString() ?? string.Empty;

            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}

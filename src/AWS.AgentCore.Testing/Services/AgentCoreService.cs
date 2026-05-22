// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using AWS.AgentCore.Testing.Models;
using Microsoft.Extensions.Options;

namespace AWS.AgentCore.Testing.Services;

public class AgentCoreService(
    IAmazonBedrockAgentCore client,
    IOptions<AgentCoreSettings> settings,
    ILogger<AgentCoreService> logger)
{
    /// <summary>
    /// Invokes the AgentCore Runtime agent with a payload and returns the full response.
    /// </summary>
    public async Task<string> InvokeAgentAsync(string jsonPayload, string? sessionId = null,
        string contentType = "application/json", CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Invoking AgentCore Runtime: {Arn}", settings.Value.RuntimeArn);

        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        var request = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = settings.Value.RuntimeArn,
            Payload = new MemoryStream(payloadBytes),
            ContentType = contentType,
            Accept = "application/json"
        };

        if (!string.IsNullOrEmpty(sessionId))
        {
            request.RuntimeSessionId = sessionId;
        }

        try
        {
            var response = await client.InvokeAgentRuntimeAsync(request, cancellationToken);

            using var reader = new StreamReader(response.Response);
            var responseBody = await reader.ReadToEndAsync(cancellationToken);

            logger.LogInformation("Received response (length={Length})", responseBody.Length);

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
            }

            return responseBody;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking AgentCore Runtime");
            throw;
        }
    }

    /// <summary>
    /// Invokes the AgentCore Runtime streaming agent with a payload and yields response chunks.
    /// </summary>
    public async IAsyncEnumerable<string> InvokeAgentStreamingAsync(
        string jsonPayload, string? sessionId = null,
        string contentType = "application/json",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var arn = settings.Value.RuntimeArn;
        if (string.IsNullOrEmpty(arn))
        {
            yield return "Error: RuntimeArn is not configured";
            yield break;
        }

        logger.LogInformation("Invoking streaming AgentCore Runtime: {Arn}", arn);

        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        var request = new InvokeAgentRuntimeRequest
        {
            AgentRuntimeArn = arn,
            Payload = new MemoryStream(payloadBytes),
            ContentType = contentType,
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
            response = await client.InvokeAgentRuntimeAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error invoking streaming AgentCore Runtime");
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

            if (chunk is null) break;
            if (chunk.Length > 0) yield return chunk;
        }
    }

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

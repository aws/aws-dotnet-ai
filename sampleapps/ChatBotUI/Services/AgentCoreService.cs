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

}

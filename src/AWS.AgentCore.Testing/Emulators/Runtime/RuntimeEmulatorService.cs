using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AWS.AgentCore.Testing.Emulators.Runtime.Models;

namespace AWS.AgentCore.Testing.Emulators.Runtime;

/// <summary>
/// Service that emulates the AgentCore Runtime by sending invocation requests
/// to the agent application and tracking session state.
/// </summary>
public class RuntimeEmulatorService(HttpClient agentClient, ILogger<RuntimeEmulatorService> logger)
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    private const int MaxPingRetries = 10;
    private static readonly TimeSpan MaxPingWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Invokes the agent with the given prompt submission.
    /// Generates or reuses a SessionId, generates a unique RequestId,
    /// pings the agent for readiness, then sends POST /invocations.
    /// </summary>
    public async Task<InvocationResult> InvokeAgentAsync(PromptSubmission submission)
    {
        var sessionId = submission.SessionId ?? Guid.NewGuid().ToString();
        var requestId = Guid.NewGuid().ToString();

        logger.LogInformation(
            "Invoking agent. SessionId: {SessionId}, RequestId: {RequestId}",
            sessionId, requestId);

        // Readiness check
        await WaitForAgentReadyAsync();

        // Build request matching AgentCore Runtime contract
        var request = new HttpRequestMessage(HttpMethod.Post, "/invocations");
        request.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id", sessionId);
        request.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id", requestId);
        request.Content = new StringContent(submission.Text, Encoding.UTF8, "application/json");

        var response = await agentClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        InvocationResult result;
        if (IsStreamingResponse(response))
        {
            result = await ConsumeStreamingResponseAsync(response, sessionId, requestId);
        }
        else
        {
            result = await ConsumeJsonResponseAsync(response, sessionId, requestId);
        }

        // Update session tracking
        UpdateSessionState(sessionId);

        return result;
    }

    /// <summary>
    /// Invokes the agent and returns the raw response stream for pass-through streaming.
    /// Used when the client wants SSE and we need to pipe the agent's SSE response directly.
    /// </summary>
    public async Task<StreamThroughResult> InvokeAgentStreamThroughAsync(PromptSubmission submission, CancellationToken cancellationToken = default)
    {
        var sessionId = submission.SessionId ?? Guid.NewGuid().ToString();
        var requestId = Guid.NewGuid().ToString();

        logger.LogInformation(
            "Stream-through invocation. SessionId: {SessionId}, RequestId: {RequestId}",
            sessionId, requestId);

        await WaitForAgentReadyAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/invocations");
        request.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id", sessionId);
        request.Headers.Add("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id", requestId);
        request.Headers.Add("Accept", "text/event-stream");
        request.Content = new StringContent(submission.Text, Encoding.UTF8, "application/json");

        var response = await agentClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        UpdateSessionState(sessionId);

        return new StreamThroughResult(sessionId, stream);
    }

    /// <summary>
    /// Waits for the agent to become ready by polling GET /ping with exponential backoff.
    /// Maximum wait time is 30 seconds.
    /// </summary>
    private async Task WaitForAgentReadyAsync()
    {
        var delay = TimeSpan.FromMilliseconds(250);
        var totalWaited = TimeSpan.Zero;

        for (var attempt = 0; attempt < MaxPingRetries; attempt++)
        {
            try
            {
                var response = await agentClient.GetAsync("/ping");
                if (response.IsSuccessStatusCode)
                {
                    logger.LogDebug("Agent is ready (ping succeeded on attempt {Attempt})", attempt + 1);
                    return;
                }

                logger.LogDebug(
                    "Agent ping returned {StatusCode} on attempt {Attempt}",
                    response.StatusCode, attempt + 1);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug(
                    "Agent ping failed on attempt {Attempt}: {Message}",
                    attempt + 1, ex.Message);
            }

            if (totalWaited + delay > MaxPingWait)
            {
                break;
            }

            await Task.Delay(delay);
            totalWaited += delay;
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxPingWait.TotalMilliseconds - totalWaited.TotalMilliseconds));
        }

        throw new TimeoutException(
            $"Agent did not become ready within {MaxPingWait.TotalSeconds} seconds. " +
            "Ensure the agent application is running and responding to GET /ping.");
    }

    /// <summary>
    /// Consumes an SSE streaming response from the agent, collecting all chunks
    /// into a single response string.
    /// </summary>
    public async Task<InvocationResult> ConsumeStreamingResponseAsync(
        HttpResponseMessage response, string sessionId, string requestId)
    {
        var responseBuilder = new StringBuilder();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            // SSE format: lines starting with "data:" contain the payload
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var data = line["data:".Length..].TrimStart();

                // "[DONE]" signals end of stream
                if (data == "[DONE]") break;

                responseBuilder.Append(data);
            }
        }

        logger.LogInformation(
            "Streaming response completed. SessionId: {SessionId}, RequestId: {RequestId}",
            sessionId, requestId);

        return new InvocationResult(
            SessionId: sessionId,
            RequestId: requestId,
            Response: responseBuilder.ToString(),
            IsStreaming: true,
            Timestamp: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Consumes a JSON response from the agent, extracting the response message.
    /// </summary>
    public async Task<InvocationResult> ConsumeJsonResponseAsync(
        HttpResponseMessage response, string sessionId, string requestId)
    {
        var content = await response.Content.ReadAsStringAsync();

        string responseText;
        try
        {
            using var doc = JsonDocument.Parse(content);
            // Try to extract a "message" or "response" field, fall back to raw content
            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                responseText = messageElement.GetString() ?? content;
            }
            else if (doc.RootElement.TryGetProperty("response", out var responseElement))
            {
                responseText = responseElement.GetString() ?? content;
            }
            else
            {
                responseText = content;
            }
        }
        catch (JsonException)
        {
            // If response is not valid JSON, use raw content
            responseText = content;
        }

        logger.LogInformation(
            "JSON response received. SessionId: {SessionId}, RequestId: {RequestId}",
            sessionId, requestId);

        return new InvocationResult(
            SessionId: sessionId,
            RequestId: requestId,
            Response: responseText,
            IsStreaming: false,
            Timestamp: DateTime.UtcNow
        );
    }

    /// <summary>
    /// Returns all tracked active sessions.
    /// </summary>
    public IReadOnlyCollection<SessionState> GetActiveSessions()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Determines if the response is a streaming (SSE) response based on content type.
    /// </summary>
    private static bool IsStreamingResponse(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        return string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates the session state tracking dictionary for the given session.
    /// </summary>
    private void UpdateSessionState(string sessionId)
    {
        var now = DateTime.UtcNow;

        _sessions.AddOrUpdate(
            sessionId,
            _ => new SessionState(
                SessionId: sessionId,
                CreatedAt: now,
                LastActivityAt: now,
                InvocationCount: 1
            ),
            (_, existing) => existing with
            {
                LastActivityAt = now,
                InvocationCount = existing.InvocationCount + 1
            }
        );
    }
}

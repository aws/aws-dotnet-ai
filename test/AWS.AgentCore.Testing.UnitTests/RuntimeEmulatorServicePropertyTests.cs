// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AWS.AgentCore.Testing.Emulators.Runtime.Models;
using AWS.AgentCore.Testing.Emulators.Runtime;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Moq;

namespace AWS.AgentCore.Testing.UnitTests;



/// <summary>
/// Property-based tests for RuntimeEmulatorService correctness properties.
/// Uses FsCheck to generate arbitrary inputs and verify universal properties.
/// Tag format: Feature: aspire-local-dev, Property {number}: {property_text}
/// </summary>
public class RuntimeEmulatorServicePropertyTests
{
    // ──────────────────────────────────────────────────────────────────
    // Property 7: Runtime Emulator Request-Id Uniqueness
    // For any sequence of N invocations (regardless of session), all
    // generated X-Amzn-Bedrock-AgentCore-Runtime-Request-Id values
    // should be unique (no two invocations share the same Request-Id).
    // **Validates: Requirements 2.3**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public async Task RequestId_AlwaysUnique_AcrossMultipleInvocations(PositiveInt countWrapper)
    {
        // Cap invocation count for test performance (2..20)
        var invocationCount = Math.Clamp(countWrapper.Get, 2, 20);

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
        var loggerMock = new Mock<ILogger<RuntimeEmulatorService>>();
        var service = new RuntimeEmulatorService(httpClient, loggerMock.Object);

        // Invoke the agent N times
        for (var i = 0; i < invocationCount; i++)
        {
            var submission = new PromptSubmission($"prompt-{i}", $"session-{i}");
            await service.InvokeAgentAsync(submission);
        }

        // Collect all Request-Id header values from POST /invocations requests
        var requestIds = handler.SentRequests
            .Where(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations")
            .Select(r => r.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id").Single())
            .ToList();

        // Verify we captured the expected number of invocations
        Assert.Equal(invocationCount, requestIds.Count);

        // Verify all Request-Ids are distinct
        var distinctCount = requestIds.Distinct().Count();
        Assert.Equal(invocationCount, distinctCount);
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 9: Runtime Emulator Concurrent Session Isolation
    // For any set of concurrent prompt submissions with distinct SessionIds,
    // each outgoing /invocations request should carry only its own SessionId
    // — no cross-contamination between concurrent sessions.
    // **Validates: Requirements 2.8**
    // ──────────────────────────────────────────────────────────────────

    [Property(MaxTest = 20)]
    public async Task ConcurrentSessions_EachRequestCarriesOwnSessionId_NoCrossContamination(PositiveInt countWrapper)
    {
        // Cap concurrent session count for test performance (2..15)
        var sessionCount = Math.Clamp(countWrapper.Get, 2, 15);

        var handler = new ThreadSafeMockHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };
        var loggerMock = new Mock<ILogger<RuntimeEmulatorService>>();
        var service = new RuntimeEmulatorService(httpClient, loggerMock.Object);

        // Generate distinct SessionIds for each concurrent submission
        var sessionIds = Enumerable.Range(0, sessionCount)
            .Select(i => $"session-{Guid.NewGuid()}-{i}")
            .ToList();

        // Submit all prompts concurrently with distinct SessionIds
        var tasks = sessionIds.Select(sessionId =>
        {
            var submission = new PromptSubmission($"prompt-for-{sessionId}", sessionId);
            return service.InvokeAgentAsync(submission);
        }).ToList();

        await Task.WhenAll(tasks);

        // Collect all POST /invocations requests with their SessionId headers
        var invocationRequests = handler.CapturedRequests
            .Where(r => r.Method == HttpMethod.Post && r.Path == "/invocations")
            .ToList();

        // Verify we captured the expected number of invocations
        Assert.Equal(sessionCount, invocationRequests.Count);

        // Verify each request carries a SessionId that belongs to our set
        // and that the prompt body matches the expected session
        foreach (var captured in invocationRequests)
        {
            var requestSessionId = captured.SessionIdHeader;
            var requestBody = captured.Body;

            // The SessionId must be one of the distinct session IDs we submitted
            Assert.Contains(requestSessionId, sessionIds);

            // The prompt body must correspond to the same session
            // (prompt text includes the session ID, so we can verify no cross-contamination)
            var expectedPrompt = $"prompt-for-{requestSessionId}";
            Assert.Contains(expectedPrompt, requestBody);
        }

        // Verify all distinct SessionIds were used (no session was lost)
        var usedSessionIds = invocationRequests
            .Select(r => r.SessionIdHeader)
            .Distinct()
            .ToList();
        Assert.Equal(sessionCount, usedSessionIds.Count);
    }

    /// <summary>
    /// A mock HttpMessageHandler that records all sent requests and returns
    /// configurable responses based on the request path.
    /// </summary>
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> SentRequests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SentRequests.Add(request);

            HttpResponseMessage response;

            if (request.RequestUri!.PathAndQuery == "/ping")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { status = "Healthy" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (request.RequestUri.PathAndQuery == "/invocations")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { message = "Agent response" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// A thread-safe mock HttpMessageHandler that captures request details
    /// (including headers and body) for concurrent test verification.
    /// </summary>
    private class ThreadSafeMockHttpMessageHandler : HttpMessageHandler
    {
        public ConcurrentBag<CapturedRequest> CapturedRequests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Read body content before it's disposed (must be done synchronously per request)
            string? body = null;
            if (request.Content != null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var sessionIdHeader = request.Headers.Contains("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id")
                ? request.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id").FirstOrDefault()
                : null;

            var captured = new CapturedRequest(
                Method: request.Method,
                Path: request.RequestUri!.PathAndQuery,
                SessionIdHeader: sessionIdHeader ?? string.Empty,
                Body: body ?? string.Empty
            );

            CapturedRequests.Add(captured);

            HttpResponseMessage response;

            if (request.RequestUri.PathAndQuery == "/ping")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { status = "Healthy" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else if (request.RequestUri.PathAndQuery == "/invocations")
            {
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { message = "Agent response" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }
            else
            {
                response = new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return response;
        }
    }

    /// <summary>
    /// Represents a captured HTTP request with relevant details for verification.
    /// </summary>
    private record CapturedRequest(
        HttpMethod Method,
        string Path,
        string SessionIdHeader,
        string Body
    );
}

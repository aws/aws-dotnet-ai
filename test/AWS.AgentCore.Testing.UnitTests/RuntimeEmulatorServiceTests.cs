// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using AWS.AgentCore.Testing.Emulators.Runtime.Models;
using AWS.AgentCore.Testing.Emulators.Runtime;
using Microsoft.Extensions.Logging;
using Moq;

namespace AWS.AgentCore.Testing.UnitTests;

public class RuntimeEmulatorServiceTests
{
    private readonly Mock<ILogger<RuntimeEmulatorService>> _loggerMock = new();

    /// <summary>
    /// Creates a RuntimeEmulatorService with a mock HttpMessageHandler that records requests
    /// and returns configurable responses.
    /// </summary>
    private (RuntimeEmulatorService service, MockHttpMessageHandler handler) CreateService()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        var service = new RuntimeEmulatorService(httpClient, _loggerMock.Object);
        return (service, handler);
    }

    [Fact]
    public async Task FormatRequest_IncludesSessionIdHeader()
    {
        // Arrange
        var (service, handler) = CreateService();
        var submission = new PromptSubmission("Hello agent", "my-session-123");

        // Act
        await service.InvokeAgentAsync(submission);

        // Assert — find the POST /invocations request
        var invocationRequest = handler.SentRequests
            .First(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");

        Assert.True(invocationRequest.Headers.Contains("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"));
        var sessionIdHeader = invocationRequest.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id").Single();
        Assert.Equal("my-session-123", sessionIdHeader);
    }

    [Fact]
    public async Task FormatRequest_IncludesRequestIdHeader()
    {
        // Arrange
        var (service, handler) = CreateService();
        var submission = new PromptSubmission("Hello agent", "session-1");

        // Act
        await service.InvokeAgentAsync(submission);

        // Assert
        var invocationRequest = handler.SentRequests
            .First(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");

        Assert.True(invocationRequest.Headers.Contains("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id"));
        var requestIdHeader = invocationRequest.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Request-Id").Single();

        // Verify it's a valid GUID
        Assert.True(Guid.TryParse(requestIdHeader, out _));
    }

    [Fact]
    public async Task FormatRequest_IncludesJsonBody()
    {
        // Arrange
        var (service, handler) = CreateService();
        var jsonPayload = """{"prompt": "What is the weather?"}""";
        var submission = new PromptSubmission(jsonPayload, "session-1");

        // Act
        await service.InvokeAgentAsync(submission);

        // Assert
        var invocationRequest = handler.SentRequests
            .First(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");

        Assert.NotNull(invocationRequest.Content);
        var bodyString = await invocationRequest.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var body = JsonDocument.Parse(bodyString);
        Assert.Equal("What is the weather?", body.RootElement.GetProperty("prompt").GetString());
    }

    [Fact]
    public async Task NullSessionId_GeneratesUuid()
    {
        // Arrange
        var (service, handler) = CreateService();
        var submission = new PromptSubmission("Hello", SessionId: null);

        // Act
        var result = await service.InvokeAgentAsync(submission);

        // Assert — the result should contain a valid UUID as SessionId
        Assert.True(Guid.TryParse(result.SessionId, out _));

        // Also verify the header was set with the generated UUID
        var invocationRequest = handler.SentRequests
            .First(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");
        var sessionIdHeader = invocationRequest.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id").Single();
        Assert.Equal(result.SessionId, sessionIdHeader);
    }

    [Fact]
    public async Task ProvidedSessionId_UsedVerbatim()
    {
        // Arrange
        var (service, handler) = CreateService();
        var providedSessionId = "custom-session-abc-123";
        var submission = new PromptSubmission("Hello", SessionId: providedSessionId);

        // Act
        var result = await service.InvokeAgentAsync(submission);

        // Assert
        Assert.Equal(providedSessionId, result.SessionId);

        var invocationRequest = handler.SentRequests
            .First(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");
        var sessionIdHeader = invocationRequest.Headers.GetValues("X-Amzn-Bedrock-AgentCore-Runtime-Session-Id").Single();
        Assert.Equal(providedSessionId, sessionIdHeader);
    }

    [Fact]
    public async Task PingBeforeInvoke_OrderCorrect()
    {
        // Arrange
        var (service, handler) = CreateService();
        var submission = new PromptSubmission("Hello", "session-1");

        // Act
        await service.InvokeAgentAsync(submission);

        // Assert — verify that GET /ping was sent before POST /invocations
        Assert.True(handler.SentRequests.Count >= 2,
            "Expected at least 2 requests (ping + invocation)");

        var pingIndex = handler.SentRequests
            .FindIndex(r => r.Method == HttpMethod.Get && r.RequestUri!.PathAndQuery == "/ping");
        var invocationIndex = handler.SentRequests
            .FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.PathAndQuery == "/invocations");

        Assert.True(pingIndex >= 0, "Expected a GET /ping request");
        Assert.True(invocationIndex >= 0, "Expected a POST /invocations request");
        Assert.True(pingIndex < invocationIndex,
            $"Expected ping (index {pingIndex}) before invocation (index {invocationIndex})");
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
                // Return a successful ping response
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
                // Return a simple JSON response
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
}

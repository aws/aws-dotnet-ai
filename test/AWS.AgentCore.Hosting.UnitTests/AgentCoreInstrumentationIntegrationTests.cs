// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.UnitTests;

[Collection("OTelIntegration")]
public class AgentCoreInstrumentationIntegrationTests : IAsyncDisposable
{
    private WebApplication? _app;

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public void AddAgentCore_AIAgent_IsWrappedWithOpenTelemetry()
    {
        var builder = WebApplication.CreateBuilder();
        var mockClient = new Mock<IChatClient>();
        builder.AddAgentCore(options => options.ChatClient = mockClient.Object);

        var sp = builder.Build().Services;
        var agent = sp.GetRequiredService<AIAgent>();

        // The AIAgent should be wrapped with OpenTelemetryAgent.
        // OpenTelemetryAgent is internal to the MS Agent Framework package,
        // so we check the type name.
        Assert.Equal("OpenTelemetryAgent", agent.GetType().Name);
    }

    [Fact]
    public async Task InvocationsEndpoint_EmitsActivitySpan()
    {
        var exportedActivities = new List<Activity>();

        var (app, client) = await CreateTestAppWithOTelAsync(
            (InstrTestRequest request) => Task.FromResult($"response: {request.Prompt}"),
            tracing: tracing => tracing.AddAgentCoreInstrumentation().AddInMemoryExporter(exportedActivities),
            metrics: null);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/invocations", new InstrTestRequest("Hi"), ct);
        response.EnsureSuccessStatusCode();

        app.Services.GetRequiredService<TracerProvider>().ForceFlush();

        Assert.Contains(exportedActivities,
            a => a.OperationName == "aws.agentcore.hosting.invocation"
                 && a.Kind == ActivityKind.Internal);
    }


    [Fact]
    public async Task InvocationsEndpoint_EmitsMetric_OnHandlerException()
    {
        var exportedActivities = new List<Activity>();

        var (app, client) = await CreateTestAppWithOTelAsync(
            (InstrTestRequest request) =>
            {
                throw new InvalidOperationException("test failure");
#pragma warning disable CS0162
                return Task.FromResult("unreachable");
#pragma warning restore CS0162
            },
            tracing: tracing => tracing.AddAgentCoreInstrumentation().AddInMemoryExporter(exportedActivities),
            metrics: null);

        var ct = TestContext.Current.CancellationToken;
        var response = await client.PostAsJsonAsync("/invocations", new InstrTestRequest("Hi"), ct);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);

        app.Services.GetRequiredService<TracerProvider>().ForceFlush();

        // The invocation span should still be emitted even when the handler throws
        Assert.Contains(exportedActivities,
            a => a.OperationName == "aws.agentcore.hosting.invocation");
    }


    private async Task<(WebApplication app, HttpClient client)> CreateTestAppWithOTelAsync(
        Delegate handler,
        Action<TracerProviderBuilder>? tracing,
        Action<MeterProviderBuilder>? metrics)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);
        builder.Services.AddLogging();

        var otel = builder.Services.AddOpenTelemetry();
        if (tracing is not null)
            otel.WithTracing(t => { t.SetSampler(new AlwaysOnSampler()); tracing(t); });
        if (metrics is not null)
            otel.WithMetrics(metrics);

        _app = builder.Build();
        _app.MapAgentCore<InstrTestRequest>(handler);

        // Force OTel providers to initialize before any requests fire.
        // The hosted service starts asynchronously; resolving the providers
        // ensures the ActivitySource/Meter listeners are subscribed.
        _app.Services.GetService<TracerProvider>();
        _app.Services.GetService<MeterProvider>();

        await _app.StartAsync();

        var client = new HttpClient
        {
            BaseAddress = new Uri(_app.Urls.First())
        };

        return (_app, client);
    }
}

public record InstrTestRequest(string? Prompt);

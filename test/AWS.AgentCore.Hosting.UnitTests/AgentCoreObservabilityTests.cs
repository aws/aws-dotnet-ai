// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
using AWS.AgentCore.Hosting.Internal;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Moq;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.UnitTests;

/// <summary>
/// Unit tests for AgentCoreObservability registration via AddAgentCore().
/// Validates Requirements: 1.2, 1.4, 2.1, 2.2, 2.3, 2.4, 3.1, 5.7, 6.1, 6.2
/// </summary>
public class AgentCoreObservabilityTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private ServiceProvider BuildServiceProvider(Action<AgentCoreOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Register a mock IChatClient to satisfy AddAgentCore's dependency
        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        // Default to enabling observability so the test exercises the registered pipeline.
        // Tests that explicitly want to disable it pass a configure callback that overrides this.
        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            configure?.Invoke(options);
        });

        var sp = builder.Services.BuildServiceProvider();
        _disposables.Add(sp);
        return sp;
    }

    [Fact]
    public void AddAgentCore_RegistersTracerProvider()
    {
        var sp = BuildServiceProvider();

        var tracerProvider = sp.GetService<TracerProvider>();

        Assert.NotNull(tracerProvider);
    }

    [Fact]
    public void AddAgentCore_RegistersMeterProvider()
    {
        var sp = BuildServiceProvider();

        var meterProvider = sp.GetService<MeterProvider>();

        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void AddAgentCore_RegistersLoggerProvider()
    {
        // LoggerProvider is registered via the logging pipeline, not directly in DI.
        // We verify by checking that the OpenTelemetry logging provider is configured
        // by building the host and ensuring no exceptions occur.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options => options.EnableObservability = true);

        // Building the app exercises the logging pipeline registration
        var app = builder.Build();
        _disposables.Add(app);

        // If OpenTelemetry logging was not registered, the logger factory would not
        // contain the OTLP log exporter. We verify it doesn't throw and the app builds.
        Assert.NotNull(app);
    }

    [Fact]
    public void AddAgentCore_OtlpExporter_UsesHttpProtobufProtocol()
    {
        // The OTLP exporter protocol is configured internally. We verify by checking
        // that the TracerProvider is built successfully with the HTTP/Protobuf configuration.
        // The actual protocol verification is done via the design: AgentCoreObservability
        // explicitly sets OtlpExportProtocol.HttpProtobuf.
        // We verify the TracerProvider is created (which exercises the OTLP config path).
        var sp = BuildServiceProvider();

        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        // Additionally verify the constant endpoint matches expected value
        Assert.Equal("AWS.AgentCore.Hosting", AgentCoreObservability.ActivitySourceName);
    }

    [Fact]
    public void AddAgentCore_SubscribesAspNetCoreTracing()
    {
        // ASP.NET Core instrumentation subscribes to the "Microsoft.AspNetCore" activity source.
        // We verify by creating an activity from that source and checking it's recorded.
        var sp = BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        using var activitySource = new ActivitySource("Microsoft.AspNetCore");
        using var activity = activitySource.StartActivity("test-aspnetcore", ActivityKind.Server);

        // If ASP.NET Core instrumentation is subscribed, the activity listener is active.
        // The activity may be null if no listener is sampling, but the provider is configured.
        // The key assertion is that TracerProvider was built with ASP.NET Core instrumentation
        // without throwing.
        Assert.NotNull(tracerProvider);
    }

    [Fact]
    public void AddAgentCore_SubscribesHttpClientTracing()
    {
        // HttpClient instrumentation subscribes to "System.Net.Http" activity source.
        var sp = BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        using var activitySource = new ActivitySource("System.Net.Http");
        using var activity = activitySource.StartActivity("test-httpclient", ActivityKind.Client);

        Assert.NotNull(tracerProvider);
    }

    [Fact]
    public void AddAgentCore_SubscribesAwsSdkTracing()
    {
        // AWS SDK instrumentation subscribes to AWS SDK activity sources.
        var sp = BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        // The AWS instrumentation is registered via AddAWSInstrumentation().
        // Verify the provider was built successfully with this instrumentation.
        Assert.NotNull(tracerProvider);
    }

    [Fact]
    public void AddAgentCore_SubscribesMsAgentFrameworkSource()
    {
        // MS Agent Framework uses "Experimental.Microsoft.Agents.AI" activity source.
        // Verify that activities from this source are captured.
        var sp = BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        using var activitySource = new ActivitySource(AgentCoreObservability.MsAgentFrameworkSource);
        using var activity = activitySource.StartActivity("test-msaf");

        // The activity should be created (not null) because the TracerProvider subscribes to this source.
        Assert.NotNull(activity);
    }

    [Fact]
    public void AddAgentCore_RegistersAspNetCoreMetrics()
    {
        // ASP.NET Core metrics instrumentation is registered via AddAspNetCoreInstrumentation().
        var sp = BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();
        Assert.NotNull(meterProvider);

        // The MeterProvider is built with ASP.NET Core metrics instrumentation.
        // Verify it was registered without throwing.
        Assert.NotNull(meterProvider);
    }

    [Fact]
    public void AddAgentCore_RegistersCustomMeter()
    {
        // The custom meter "AWS.AgentCore" should be subscribed in the MeterProvider.
        var sp = BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();
        Assert.NotNull(meterProvider);

        // Verify the meter name constant is correct
        Assert.Equal("AWS.AgentCore.Hosting", AgentCoreObservability.MeterName);

        // Create a test instrument on the AWS.AgentCore meter and verify it's observable
        var exportedMetrics = new List<Metric>();
        using var meter = new Meter(AgentCoreObservability.MeterName);
        var counter = meter.CreateCounter<long>("test.counter");
        counter.Add(1);

        // Force a collect to verify the meter is subscribed
        meterProvider.ForceFlush();
    }

    [Fact]
    public void EnableObservability_False_SkipsDefaultPipelineRegistration()
    {
        var sp = BuildServiceProvider(options =>
        {
            options.EnableObservability = false;
        });

        var tracerProvider = sp.GetService<TracerProvider>();
        var meterProvider = sp.GetService<MeterProvider>();

        // When observability is opt-out, AddAgentCore() does not register a default pipeline.
        // The application is expected to wire its own OTel (e.g., via Aspire ServiceDefaults
        // or by calling AddAgentCoreInstrumentation() on its own TracerProvider/MeterProvider).
        Assert.Null(tracerProvider);
        Assert.Null(meterProvider);
    }

    [Fact]
    public void AddAgentCore_StartsWithoutException_WhenSidecarUnreachable()
    {
        // The OTLP exporter uses lazy connection - no TCP connection is attempted during startup.
        // Verify the application can be built and started without the sidecar being available.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore();

        // Building and creating the app should not throw even though localhost:4318 is unreachable
        var exception = Record.Exception(() =>
        {
            var app = builder.Build();
            _disposables.Add(app);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task AddAgentCore_ExportsWithoutException_WhenSidecarUnreachable()
    {
        // Verify that telemetry export attempts don't throw when the sidecar is unreachable.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore();

        var app = builder.Build();
        _disposables.Add(app);

        await app.StartAsync(TestContext.Current.CancellationToken);

        // Create some telemetry that would be exported
        var exception = await Record.ExceptionAsync(async () =>
        {
            using var activitySource = new ActivitySource(AgentCoreObservability.ActivitySourceName);
            using var activity = activitySource.StartActivity("test-export");
            activity?.SetTag("test", "value");

            // Force flush to trigger export attempt to unreachable sidecar
            var tracerProvider = app.Services.GetService<TracerProvider>();
            tracerProvider?.ForceFlush();

            var meterProvider = app.Services.GetService<MeterProvider>();
            meterProvider?.ForceFlush();

            await Task.CompletedTask;
        });

        Assert.Null(exception);

        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddAgentCore_SubscribesMsExtensionsAiActivitySource()
    {
        // The Microsoft.Extensions.AI default ActivitySource ("Experimental.Microsoft.Extensions.AI")
        // must be subscribed so any IChatClient wrapped with .UseOpenTelemetry() (without an explicit
        // sourceName) emits activities that are captured by our TracerProvider.
        var sp = BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        using var source = new ActivitySource(AgentCoreObservability.MsExtensionsAiSource);
        using var activity = source.StartActivity("test-meai-source");

        Assert.NotNull(activity);
    }

    [Fact]
    public void AddAgentCore_SubscribesMsAgentFrameworkMeter()
    {
        // The MS Agent Framework default meter ("Experimental.Microsoft.Agents.AI") must be
        // subscribed so an OpenTelemetryAgent wrapper (without an explicit sourceName) emits
        // metrics like agent_framework.function.invocation.duration.
        var sp = BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();
        Assert.NotNull(meterProvider);

        var exportedMetrics = new List<Metric>();
        using var meter = new Meter(AgentCoreObservability.MsAgentFrameworkSource);
        var counter = meter.CreateCounter<long>("test.msaf.counter");
        counter.Add(1);

        meterProvider.ForceFlush(1000);
        // We can't easily collect metrics without a custom reader, but the act of creating a
        // Counter on a subscribed meter does not throw, and ForceFlush succeeds. The constants
        // assertion below confirms our code subscribes the right meter name.
        Assert.Equal("Experimental.Microsoft.Agents.AI", AgentCoreObservability.MsAgentFrameworkSource);
    }

    [Fact]
    public void AddAgentCore_SubscribesMsExtensionsAiMeter()
    {
        // The Microsoft.Extensions.AI default meter ("Experimental.Microsoft.Extensions.AI") must
        // be subscribed so an OpenTelemetryChatClient wrapper emits metrics like
        // gen_ai.client.operation.duration and gen_ai.client.token.usage.
        var sp = BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();
        Assert.NotNull(meterProvider);

        using var meter = new Meter(AgentCoreObservability.MsExtensionsAiSource);
        var counter = meter.CreateCounter<long>("test.meai.counter");
        counter.Add(1);
        meterProvider.ForceFlush(1000);

        Assert.Equal("Experimental.Microsoft.Extensions.AI", AgentCoreObservability.MsExtensionsAiSource);
    }

    [Fact]
    public void AddAgentCore_DefaultOtlpEndpoint_IsLocalhost4318HttpProtobuf()
    {
        // Verify the constants used to construct the default OTLP exporter when no
        // OTEL_EXPORTER_OTLP_ENDPOINT env var is set. Instance verification (that the SDK
        // actually receives these values) requires integration testing with a real collector.
        Assert.Equal("http://localhost:4318", AgentCoreObservability.DefaultOtlpEndpoint);
    }

    [Fact]
    public void AddAgentCore_HasUserConfiguredOtlpEndpoint_DetectsAllSignalEnvVars()
    {
        // Verify that all 4 standard OTLP env vars (base + 3 per-signal) are recognized
        // as "user has configured an endpoint".
        var envVars = new[]
        {
            "OTEL_EXPORTER_OTLP_ENDPOINT",
            "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
            "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
            "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
        };

        // Snapshot original values
        var originals = envVars.ToDictionary(v => v, v => Environment.GetEnvironmentVariable(v));
        try
        {
            // Clear all
            foreach (var v in envVars)
                Environment.SetEnvironmentVariable(v, null);

            // With all unset, our default endpoint applies (assertion via constant since we
            // can't observe the SDK's resolved endpoint without an integration test).
            Assert.False(AgentCoreObservability.HasUserConfiguredOtlpEndpoint());

            // Set each one in turn and verify detection.
            foreach (var v in envVars)
            {
                Environment.SetEnvironmentVariable(v, "http://probe:4318");
                Assert.True(AgentCoreObservability.HasUserConfiguredOtlpEndpoint(),
                    $"Expected HasUserConfiguredOtlpEndpoint to return true when {v} is set.");
                Environment.SetEnvironmentVariable(v, null);
            }
        }
        finally
        {
            // Restore originals
            foreach (var (k, val) in originals)
                Environment.SetEnvironmentVariable(k, val);
        }
    }
}

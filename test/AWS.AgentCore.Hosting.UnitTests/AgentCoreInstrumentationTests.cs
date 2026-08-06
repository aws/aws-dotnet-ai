// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
using AWS.AgentCore.Hosting.Internal;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.UnitTests;

[Collection("OTelIntegration")]
public class AgentCoreInstrumentationTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var d in _disposables)
            d.Dispose();
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Tracing_SubscribesAgentCoreSource()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using (AgentCoreActivitySource.StartInvocation(null)) { }
        tracerProvider.ForceFlush();

        Assert.Single(exportedActivities);
        Assert.Equal("aws.agentcore.hosting.invocation", exportedActivities[0].OperationName);
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Tracing_SubscribesMsAgentFrameworkSource()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using var source = new ActivitySource(AgentCoreObservability.MsAgentFrameworkSource);
        using (source.StartActivity("test-agent-span")) { }
        tracerProvider.ForceFlush();

        Assert.Single(exportedActivities);
        Assert.Equal("test-agent-span", exportedActivities[0].OperationName);
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Tracing_SubscribesMsExtensionsAiSource()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using var source = new ActivitySource(AgentCoreObservability.MsExtensionsAiSource);
        using (source.StartActivity("test-chat-span")) { }
        tracerProvider.ForceFlush();

        Assert.Single(exportedActivities);
        Assert.Equal("test-chat-span", exportedActivities[0].OperationName);
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Metrics_SubscribesAgentCoreMeter()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        AgentCoreMetrics.RecordInvocationDuration(1.5, null);
        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.Name == "gen_ai.client.operation.duration");
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Metrics_SubscribesMsAgentFrameworkMeter()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        using var meter = new Meter(AgentCoreObservability.MsAgentFrameworkSource);
        var counter = meter.CreateCounter<int>("test.counter");
        counter.Add(1);
        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.Name == "test.counter");
    }

    [Fact]
    public void AddAgentCoreInstrumentation_Metrics_SubscribesMsExtensionsAiMeter()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        using var meter = new Meter(AgentCoreObservability.MsExtensionsAiSource);
        var counter = meter.CreateCounter<int>("test.ai.counter");
        counter.Add(1);
        meterProvider.ForceFlush();

        Assert.Contains(exportedMetrics, m => m.Name == "test.ai.counter");
    }

    [Fact]
    public void StartInvocation_CreatesInternalKindActivity()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using (AgentCoreActivitySource.StartInvocation("session-123")) { }
        tracerProvider.ForceFlush();

        var activity = Assert.Single(exportedActivities);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
    }

    [Fact]
    public void StartInvocation_SetsConversationIdTag_WhenProvided()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using (AgentCoreActivitySource.StartInvocation("session-abc")) { }
        tracerProvider.ForceFlush();

        var activity = Assert.Single(exportedActivities);
        Assert.Equal("session-abc", activity.GetTagItem("gen_ai.conversation.id"));
    }

    [Fact]
    public void StartInvocation_OmitsConversationIdTag_WhenNull()
    {
        var exportedActivities = new List<Activity>();
        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedActivities)
            .Build()!;
        _disposables.Add(tracerProvider);

        using (AgentCoreActivitySource.StartInvocation(null)) { }
        tracerProvider.ForceFlush();

        var activity = Assert.Single(exportedActivities);
        Assert.Null(activity.GetTagItem("gen_ai.conversation.id"));
    }

    [Fact]
    public void RecordInvocationDuration_EmitsOperationNameTag()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        AgentCoreMetrics.RecordInvocationDuration(2.0, null);
        meterProvider.ForceFlush();

        var metric = exportedMetrics.First(m => m.Name == "gen_ai.client.operation.duration");
        var points = GetMetricPointTags(metric);
        Assert.Contains(points, tags => tags.Any(t => t.Key == "gen_ai.operation.name" && (string)t.Value! == "invoke_agent"));
    }

    [Fact]
    public void RecordInvocationDuration_EmitsErrorTypeTag_WhenFailed()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        AgentCoreMetrics.RecordInvocationDuration(1.0, "System.TimeoutException");
        meterProvider.ForceFlush();

        var metric = exportedMetrics.First(m => m.Name == "gen_ai.client.operation.duration");
        var points = GetMetricPointTags(metric);
        Assert.Contains(points, tags => tags.Any(t => t.Key == "error.type" && (string)t.Value! == "System.TimeoutException"));
    }

    [Fact]
    public void RecordMemoryLoad_EmitsSearchMemoryOperation()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        AgentCoreMetrics.RecordMemoryLoad();
        meterProvider.ForceFlush();

        var metric = exportedMetrics.First(m => m.Name == "gen_ai.client.operation.duration");
        var points = GetMetricPointTags(metric);
        Assert.Contains(points, tags => tags.Any(t => t.Key == "gen_ai.operation.name" && (string)t.Value! == "search_memory"));
    }

    [Fact]
    public void RecordMemorySave_EmitsUpsertMemoryOperation()
    {
        var exportedMetrics = new List<Metric>();
        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddAgentCoreInstrumentation()
            .AddInMemoryExporter(exportedMetrics)
            .Build()!;
        _disposables.Add(meterProvider);

        AgentCoreMetrics.RecordMemorySave();
        meterProvider.ForceFlush();

        var metric = exportedMetrics.First(m => m.Name == "gen_ai.client.operation.duration");
        var points = GetMetricPointTags(metric);
        Assert.Contains(points, tags => tags.Any(t => t.Key == "gen_ai.operation.name" && (string)t.Value! == "upsert_memory"));
    }

    // Returns the tag-set of every metric point. AgentCoreMetrics uses a process-global
    // static Meter, so points recorded by other tests running in parallel can appear here
    // too. Callers must therefore match against all points rather than assuming a single one.
    private static List<List<KeyValuePair<string, object?>>> GetMetricPointTags(Metric metric)
    {
        var points = new List<List<KeyValuePair<string, object?>>>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            var tags = new List<KeyValuePair<string, object?>>();
            foreach (var tag in point.Tags)
                tags.Add(tag);
            points.Add(tags);
        }

        Assert.NotEmpty(points);
        return points;
    }
}

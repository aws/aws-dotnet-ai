// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Diagnostics.Metrics;
using AWS.AgentCore.Hosting.Internal;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AWS.AgentCore.Hosting.UnitTests;

/// <summary>
/// Property-based tests for AgentCore OpenTelemetry observability correctness properties.
/// Uses FsCheck to generate arbitrary inputs and verify universal properties.
/// Tag format: Feature: agentcore-opentelemetry, Property {number}: {property_text}
/// </summary>
public class AgentCoreObservabilityPropertyTests : IDisposable
{
    private readonly List<IDisposable> _disposables = new();

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    // Helper to generate a valid activity source name from a seed
    private static string GenerateActivitySourceName(int seed)
        => "Custom.Source" + Math.Abs(seed % 10000);

    // Helper to generate a valid meter name from a seed
    private static string GenerateMeterName(int seed)
        => "CustomMeter.Meter" + Math.Abs(seed % 10000);

    // Helper to generate an alphanumeric key from a seed
    private static string GenerateAlphanumericKey(int seed)
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var s = Math.Abs(seed);
        var result = "";
        for (int i = 0; i < 5; i++)
        {
            result += chars[(s + i) % chars.Length];
            s /= chars.Length;
        }
        return "Key" + result;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 1: OTLP Endpoint Resolution
    // For any AgentCoreOptions where EnableObservability is true, the
    // resolved OTLP endpoint is the user-specified endpoint (via env vars)
    // if provided, otherwise http://localhost:4318.
    // Validates: Requirements 1.1, 1.3
    // ──────────────────────────────────────────────────────────────────

    // ──────────────────────────────────────────────────────────────────
    // Property 2: Custom Activity Sources Subscription
    // For any list of valid activity source names provided via the
    // ConfigureTracing callback, all provided source names SHALL be
    // subscribed in the TracerProvider in addition to the default sources.
    // Validates: Requirements 2.5
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 2.5**
    /// For any valid custom activity source name provided via ConfigureTracing,
    /// the TracerProvider subscribes to that source (StartActivity returns non-null).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Custom activity sources subscription")]
    public bool CustomActivitySources_AreSubscribed_WhenAddedViaConfigureTracing(
        PositiveInt seed)
    {
        var sourceName = GenerateActivitySourceName(seed.Get);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing => tracing.AddSource(sourceName);
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();

        if (tracerProvider == null)
            return false;

        using var activitySource = new ActivitySource(sourceName);
        using var activity = activitySource.StartActivity("test-operation");

        return activity != null;
    }

    /// <summary>
    /// **Validates: Requirements 2.5**
    /// For any valid custom activity source name provided via ConfigureTracing,
    /// the default activity sources ("AWS.AgentCore" and "Experimental.Microsoft.Agents.AI")
    /// remain subscribed alongside the custom source.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Custom activity sources subscription")]
    public bool CustomActivitySources_DefaultSourcesRemainSubscribed_WhenCustomSourceAdded(
        PositiveInt seed)
    {
        var sourceName = GenerateActivitySourceName(seed.Get);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing => tracing.AddSource(sourceName);
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();

        if (tracerProvider == null)
            return false;

        using var agentCoreSource = new ActivitySource(AgentCoreObservability.ActivitySourceName);
        using var agentCoreActivity = agentCoreSource.StartActivity("test-default-agentcore");

        using var msAfSource = new ActivitySource(AgentCoreObservability.MsAgentFrameworkSource);
        using var msAfActivity = msAfSource.StartActivity("test-default-msaf");

        return agentCoreActivity != null && msAfActivity != null;
    }

    /// <summary>
    /// **Validates: Requirements 2.5**
    /// For any list of multiple valid activity source names provided via ConfigureTracing,
    /// all provided source names are subscribed in the TracerProvider.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Custom activity sources subscription")]
    public bool CustomActivitySources_MultipleSourcesAllSubscribed(
        PositiveInt seed1, PositiveInt seed2, PositiveInt seed3)
    {
        var sourceNames = new[]
        {
            "Custom.Multi.A" + Math.Abs(seed1.Get % 1000),
            "Custom.Multi.B" + Math.Abs(seed2.Get % 1000),
            "Custom.Multi.C" + Math.Abs(seed3.Get % 1000)
        }.Distinct().ToList();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing =>
            {
                foreach (var name in sourceNames)
                {
                    tracing.AddSource(name);
                }
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();

        if (tracerProvider == null)
            return false;

        var activitySources = sourceNames
            .Select(name => new ActivitySource(name))
            .ToList();

        try
        {
            foreach (var source in activitySources)
            {
                using var activity = source.StartActivity("test-operation");
                if (activity == null)
                    return false;
            }

            return true;
        }
        finally
        {
            foreach (var source in activitySources)
            {
                source.Dispose();
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 3: Custom Meters Subscription
    // For any valid meter name provided via the ConfigureMetrics callback,
    // the MeterProvider SHALL subscribe to that meter in addition to the
    // default meters.
    // Validates: Requirements 3.5
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.5**
    /// For any valid custom meter name provided via ConfigureMetrics,
    /// the MeterProvider subscribes to that meter (metrics are collected).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Custom meters subscription")]
    public bool CustomMeters_AreSubscribed_WhenAddedViaConfigureMetrics(
        PositiveInt seed)
    {
        var meterName = GenerateMeterName(seed.Get);
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureMetrics = metrics =>
            {
                metrics.AddMeter(meterName);
                metrics.AddInMemoryExporter(exportedMetrics);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();

        if (meterProvider == null)
            return false;

        using var meter = new Meter(meterName);
        var counter = meter.CreateCounter<long>("test-counter");
        counter.Add(1);

        meterProvider.ForceFlush();

        return exportedMetrics.Any(m => m.MeterName == meterName);
    }

    /// <summary>
    /// **Validates: Requirements 3.5**
    /// For any valid custom meter name provided via ConfigureMetrics,
    /// the default meter ("AWS.AgentCore") remains subscribed alongside the custom meter.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Custom meters subscription")]
    public bool CustomMeters_DefaultMeterRemainsSubscribed_WhenCustomMeterAdded(
        PositiveInt seed)
    {
        var meterName = GenerateMeterName(seed.Get);
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureMetrics = metrics =>
            {
                metrics.AddMeter(meterName);
                metrics.AddInMemoryExporter(exportedMetrics);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();

        if (meterProvider == null)
            return false;

        using var defaultMeter = new Meter(AgentCoreObservability.MeterName);
        var defaultCounter = defaultMeter.CreateCounter<long>("test-default-counter");
        defaultCounter.Add(1);

        using var customMeter = new Meter(meterName);
        var customCounter = customMeter.CreateCounter<long>("test-custom-counter");
        customCounter.Add(1);

        meterProvider.ForceFlush();

        var hasDefaultMeter = exportedMetrics.Any(m => m.MeterName == AgentCoreObservability.MeterName);
        var hasCustomMeter = exportedMetrics.Any(m => m.MeterName == meterName);

        return hasDefaultMeter && hasCustomMeter;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 4: Structured Log Properties Preservation
    // For any set of key-value pairs logged as structured properties via
    // ILogger, the exported OTLP log record SHALL contain all of those
    // key-value pairs.
    // Validates: Requirements 4.2
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.2**
    /// For any set of key-value pairs logged as structured properties via ILogger,
    /// the exported OTLP log record contains all of those key-value pairs.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Structured log properties preservation")]
    public bool StructuredLogProperties_ArePreservedInExportedLogRecords(
        PositiveInt seed1, PositiveInt seed2, PositiveInt seed3)
    {
        // Generate 1-3 key-value pairs from the seeds
        var properties = new Dictionary<string, string>
        {
            [GenerateAlphanumericKey(seed1.Get)] = "Value" + Math.Abs(seed1.Get % 1000),
            [GenerateAlphanumericKey(seed2.Get + 100)] = "Value" + Math.Abs(seed2.Get % 1000),
            [GenerateAlphanumericKey(seed3.Get + 200)] = "Value" + Math.Abs(seed3.Get % 1000)
        };

        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.StructuredLog");

        var keys = properties.Keys.ToList();
        var values = properties.Values.ToArray();
        var templateParts = keys.Select(k => $"{{{k}}}");
        var messageTemplate = "Test message " + string.Join(" ", templateParts);

        logger.LogInformation(messageTemplate, values);

        if (logRecords.Count == 0)
            return false;

        var logRecord = logRecords[^1];

        var stateValues = logRecord.Attributes;
        if (stateValues == null)
            return false;

        var stateDict = stateValues.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? "");

        foreach (var kvp in properties)
        {
            if (!stateDict.TryGetValue(kvp.Key, out var recordedValue))
                return false;

            if (recordedValue != kvp.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// **Validates: Requirements 4.2**
    /// For any single key-value pair logged as a structured property via ILogger,
    /// the exported OTLP log record contains that key-value pair.
    /// This tests the minimal case of a single structured property.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Structured log properties preservation")]
    public bool StructuredLogProperties_SingleProperty_IsPreservedInExportedLogRecord(
        PositiveInt seed,
        NonEmptyString value)
    {
        var keyStr = GenerateAlphanumericKey(seed.Get);
        var valueStr = value.Get;
        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.SingleStructuredLog");

        logger.LogInformation("Message {" + keyStr + "}", valueStr);

        if (logRecords.Count == 0)
            return false;

        var logRecord = logRecords[^1];
        var stateValues = logRecord.Attributes;
        if (stateValues == null)
            return false;

        var stateDict = stateValues.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToString() ?? "");

        return stateDict.TryGetValue(keyStr, out var recordedValue)
            && recordedValue == valueStr;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 5: Log-Trace Correlation
    // For any active trace context (TraceId, SpanId), log records emitted
    // within that context SHALL contain the matching TraceId and SpanId
    // in the exported OTLP log record.
    // Validates: Requirements 4.3
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.3**
    /// For any active trace context (TraceId, SpanId), log records emitted within that context
    /// contain the matching TraceId and SpanId in the exported OTLP log record.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Log-trace correlation")]
    public bool LogTraceCorrelation_LogRecordContainsMatchingTraceIdAndSpanId(
        PositiveInt seed)
    {
        var sourceName = GenerateActivitySourceName(seed.Get);
        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing => tracing.AddSource(sourceName);
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.LogTraceCorrelation");

        if (tracerProvider == null)
            return false;

        using var activitySource = new ActivitySource(sourceName);
        using var activity = activitySource.StartActivity("test-operation");

        if (activity == null)
            return false;

        var expectedTraceId = activity.TraceId;
        var expectedSpanId = activity.SpanId;

        logger.LogInformation("Test log message within trace context for {ActivityName}", sourceName);

        if (logRecords.Count == 0)
            return false;

        var logRecord = logRecords[^1];

        return logRecord.TraceId == expectedTraceId
            && logRecord.SpanId == expectedSpanId;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    /// When no trace context is active, log records should have default (empty) TraceId and SpanId.
    /// This confirms correlation only happens when a trace is actually active.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Log-trace correlation")]
    public bool LogTraceCorrelation_NoActiveTrace_LogRecordHasDefaultTraceIdAndSpanId(
        NonEmptyString logMessage)
    {
        var message = logMessage.Get;
        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.LogTraceCorrelation.NoTrace");

        Activity.Current = null;

        logger.LogInformation("No trace context: {Message}", message);

        if (logRecords.Count == 0)
            return false;

        var logRecord = logRecords[^1];

        return logRecord.TraceId == default
            && logRecord.SpanId == default;
    }

    /// <summary>
    /// **Validates: Requirements 4.3**
    /// For any active trace context, multiple log records emitted within the same span
    /// all contain the same TraceId and SpanId matching the active activity.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Log-trace correlation")]
    public bool LogTraceCorrelation_MultipleLogsInSameSpan_AllHaveMatchingIds(
        PositiveInt seed)
    {
        var sourceName = GenerateActivitySourceName(seed.Get);
        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing => tracing.AddSource(sourceName);
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.LogTraceCorrelation.Multiple");

        if (tracerProvider == null)
            return false;

        using var activitySource = new ActivitySource(sourceName);
        using var activity = activitySource.StartActivity("test-multi-log-operation");

        if (activity == null)
            return false;

        var expectedTraceId = activity.TraceId;
        var expectedSpanId = activity.SpanId;

        logger.LogInformation("First log in span");
        logger.LogWarning("Second log in span");
        logger.LogError("Third log in span");

        if (logRecords.Count < 3)
            return false;

        return logRecords.All(record =>
            record.TraceId == expectedTraceId
            && record.SpanId == expectedSpanId);
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 6: Log Level Filtering
    // For any configured minimum log level L and any log entry with level E,
    // the entry is exported if and only if E >= L.
    // Validates: Requirements 4.4
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.4**
    /// For any configured minimum log level L and any log entry with level E,
    /// the entry is exported via OTLP if and only if E >= L.
    /// LogLevel.None is excluded for the entry level since you cannot log at None.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Log level filtering")]
    public bool LogLevelFiltering_EntryExportedIfAndOnlyIfLevelAtOrAboveMinimum(
        PositiveInt minLevelSeed, PositiveInt entryLevelSeed)
    {
        var allLogLevels = new[]
        {
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Information,
            LogLevel.Warning,
            LogLevel.Error,
            LogLevel.Critical,
            LogLevel.None
        };

        var loggableLogLevels = new[]
        {
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Information,
            LogLevel.Warning,
            LogLevel.Error,
            LogLevel.Critical
        };

        var minimumLevel = allLogLevels[minLevelSeed.Get % allLogLevels.Length];
        var entryLevel = loggableLogLevels[entryLevelSeed.Get % loggableLogLevels.Length];

        var logRecords = new List<LogRecord>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.Logging.SetMinimumLevel(minimumLevel);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureLogging = logging =>
            {
                logging.AddInMemoryExporter(logRecords);
            };
        });

        using var sp = builder.Services.BuildServiceProvider();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PropertyTest.LogLevelFiltering");

        logger.Log(entryLevel, "Test message at level {Level}", entryLevel);

        var shouldBeExported = entryLevel >= minimumLevel;
        var wasExported = logRecords.Count > 0;

        return wasExported == shouldBeExported;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 7: Additive Composition of Customization
    // For any set of activity sources added via the ConfigureTracing
    // inline callback and any set added via the standard
    // ConfigureOpenTelemetryTracerProvider API, the TracerProvider SHALL
    // subscribe to the union of all sources from both mechanisms.
    // Validates: Requirements 5.5
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 5.5**
    /// For any set of activity sources added via ConfigureTracing inline callback and any set
    /// added via the standard ConfigureOpenTelemetryTracerProvider API, the TracerProvider
    /// subscribes to the union of all sources from both mechanisms.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Additive composition of customization")]
    public bool AdditiveComposition_BothInlineAndStandardApiSources_AreSubscribed(
        PositiveInt seed1, PositiveInt seed2)
    {
        var inlineSources = new List<string>
        {
            "Inline.Source" + Math.Abs(seed1.Get % 1000),
            "Inline.Source" + Math.Abs((seed1.Get + 1) % 1000)
        }.Distinct().ToList();

        var standardApiSources = new List<string>
        {
            "Standard.Source" + Math.Abs(seed2.Get % 1000),
            "Standard.Source" + Math.Abs((seed2.Get + 1) % 1000)
        }.Distinct().ToList();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing =>
            {
                foreach (var source in inlineSources)
                {
                    tracing.AddSource(source);
                }
            };
        });

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
        {
            foreach (var source in standardApiSources)
            {
                tracing.AddSource(source);
            }
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();

        if (tracerProvider == null)
            return false;

        var inlineActivitySources = inlineSources
            .Select(name => new ActivitySource(name))
            .ToList();

        var standardActivitySources = standardApiSources
            .Select(name => new ActivitySource(name))
            .ToList();

        try
        {
            foreach (var source in inlineActivitySources)
            {
                using var activity = source.StartActivity("test-inline-operation");
                if (activity == null)
                    return false;
            }

            foreach (var source in standardActivitySources)
            {
                using var activity = source.StartActivity("test-standard-api-operation");
                if (activity == null)
                    return false;
            }

            return true;
        }
        finally
        {
            foreach (var source in inlineActivitySources)
                source.Dispose();
            foreach (var source in standardActivitySources)
                source.Dispose();
        }
    }

    /// <summary>
    /// **Validates: Requirements 5.5**
    /// For any set of activity sources added via both mechanisms, the default sources
    /// (AWS.AgentCore and MS Agent Framework) remain subscribed alongside both sets.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Feature", "agentcore-opentelemetry")]
    [Trait("Property", "Additive composition of customization")]
    public bool AdditiveComposition_DefaultSourcesRemainSubscribed_WhenBothMechanismsUsed(
        PositiveInt seed1, PositiveInt seed2)
    {
        var inlineSources = new List<string>
        {
            "Inline.Src" + Math.Abs(seed1.Get % 1000)
        };

        var standardApiSources = new List<string>
        {
            "Standard.Src" + Math.Abs(seed2.Get % 1000)
        };

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var mockChatClient = new Mock<IChatClient>();
        builder.Services.AddSingleton(mockChatClient.Object);

        builder.AddAgentCore(options =>
        {
            options.EnableObservability = true;
            options.ConfigureTracing = tracing =>
            {
                foreach (var source in inlineSources)
                {
                    tracing.AddSource(source);
                }
            };
        });

        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
        {
            foreach (var source in standardApiSources)
            {
                tracing.AddSource(source);
            }
        });

        using var sp = builder.Services.BuildServiceProvider();
        var tracerProvider = sp.GetService<TracerProvider>();

        if (tracerProvider == null)
            return false;

        using var agentCoreSource = new ActivitySource(AgentCoreObservability.ActivitySourceName);
        using var agentCoreActivity = agentCoreSource.StartActivity("test-default-agentcore");

        using var msAfSource = new ActivitySource(AgentCoreObservability.MsAgentFrameworkSource);
        using var msAfActivity = msAfSource.StartActivity("test-default-msaf");

        return agentCoreActivity != null && msAfActivity != null;
    }
}

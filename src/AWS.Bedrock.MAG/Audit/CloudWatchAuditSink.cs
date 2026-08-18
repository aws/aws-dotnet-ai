// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using AWS.Logger;
using AWS.Logger.Core;
using AgentGovernance.Audit;

namespace AWS.Bedrock.MAG.Audit
{
    /// <summary>
    /// Subscribes to the toolkit's <see cref="AuditEmitter"/> and writes governance events to CloudWatch.
    /// Log delivery is delegated to AWS.Logger.Core (batching, retries, log group/stream creation); this
    /// sink only formats events and aggregates governance metrics. The event handler does no blocking I/O
    /// and never throws, so a sink hiccup can't break the governance loop.
    /// </summary>
    public sealed class CloudWatchAuditSink : IDisposable
    {
        private const string LogType = "AWS.Bedrock.MAG";

        private readonly CloudWatchAuditOptions _options;
        private readonly IAWSLoggerCore _logger;
        private readonly IAmazonCloudWatch? _metrics;

        private readonly object _metricLock = new();
        private readonly Dictionary<MetricKey, int> _metricCounts = new();
        private readonly Timer? _metricTimer;

        private AuditEmitter? _emitter;
        private Action<GovernanceEvent>? _handler;
        private int _disposed;

        /// <summary>
        /// Creates a sink. Pass a logger/metrics client for tests or custom credentials; otherwise they are
        /// built from <see cref="CloudWatchAuditOptions"/>. The metrics client and timer are only created
        /// when <see cref="CloudWatchAuditOptions.EmitMetrics"/> is true.
        /// </summary>
        public CloudWatchAuditSink(
            CloudWatchAuditOptions options,
            IAWSLoggerCore? logger = null,
            IAmazonCloudWatch? metricsClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? CreateLogger(options);

            if (options.EmitMetrics)
            {
                _metrics = metricsClient ?? CreateMetricsClient(options.Region);
                _metricTimer = new Timer(_ => _ = FlushMetricsGuardedAsync(), null, options.FlushInterval, options.FlushInterval);
            }
        }

        /// <summary>Subscribes this sink to an emitter. Call once; events flow until the sink is disposed.</summary>
        public void Subscribe(AuditEmitter emitter)
        {
            ArgumentNullException.ThrowIfNull(emitter);
            _emitter = emitter;
            _handler = OnEvent;
            emitter.OnAll(_handler);
        }

        // Never throws and does no blocking I/O: hand the formatted line to AWS.Logger.Core's in-memory
        // queue and bump the in-memory metric counters. All network I/O happens on background threads.
        private void OnEvent(GovernanceEvent governanceEvent)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _logger.AddMessage(GovernanceEventSerializer.Serialize(governanceEvent));

            if (_metrics is not null)
            {
                var metric = MapMetricName(governanceEvent.Type);
                if (metric is not null)
                {
                    var key = new MetricKey(metric, governanceEvent.AgentId, governanceEvent.PolicyName);
                    lock (_metricLock)
                    {
                        _metricCounts[key] = _metricCounts.TryGetValue(key, out var count) ? count + 1 : 1;
                    }
                }
            }
        }

        /// <summary>
        /// Publishes the aggregated governance counters to CloudWatch metrics and resets them. Called on the
        /// flush timer and at dispose; safe to call directly. No-op when metrics are disabled.
        /// </summary>
        public async Task FlushMetricsAsync(CancellationToken cancellationToken = default)
        {
            if (_metrics is null)
            {
                return;
            }

            List<KeyValuePair<MetricKey, int>> snapshot;
            lock (_metricLock)
            {
                if (_metricCounts.Count == 0)
                {
                    return;
                }

                snapshot = _metricCounts.ToList();
                _metricCounts.Clear();
            }

            var data = snapshot.Select(pair =>
            {
                var dimensions = new List<Dimension> { new() { Name = "AgentId", Value = pair.Key.AgentId } };
                if (!string.IsNullOrEmpty(pair.Key.PolicyName))
                {
                    dimensions.Add(new Dimension { Name = "PolicyName", Value = pair.Key.PolicyName });
                }

                return new MetricDatum
                {
                    MetricName = pair.Key.Metric,
                    Value = pair.Value,
                    Unit = Amazon.CloudWatch.StandardUnit.Count,
                    Dimensions = dimensions
                };
            }).ToList();

            // CloudWatch accepts at most 1000 metric data items per PutMetricData call.
            const int MaxPerCall = 1000;
            for (var i = 0; i < data.Count; i += MaxPerCall)
            {
                var slice = data.GetRange(i, Math.Min(MaxPerCall, data.Count - i));
                await _metrics.PutMetricDataAsync(new PutMetricDataRequest
                {
                    Namespace = _options.MetricNamespace,
                    MetricData = slice
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task FlushMetricsGuardedAsync()
        {
            try
            {
                await FlushMetricsAsync().ConfigureAwait(false);
            }
            catch
            {
                // Audit must never break the governance loop. Drop the metric batch on persistent failure.
            }
        }

        private static string? MapMetricName(GovernanceEventType type) => type switch
        {
            GovernanceEventType.PolicyCheck => "PolicyChecks",
            GovernanceEventType.PolicyViolation => "PolicyViolations",
            GovernanceEventType.ToolCallBlocked => "ToolCallsBlocked",
            GovernanceEventType.TrustFailed => "TrustFailures",
            _ => null
        };

        private static IAWSLoggerCore CreateLogger(CloudWatchAuditOptions options)
        {
            var config = new AWSLoggerConfig(options.LogGroupName)
            {
                BatchPushInterval = options.FlushInterval
            };

            if (options.Region is not null)
            {
                config.Region = options.Region.SystemName;
            }

            if (!string.IsNullOrWhiteSpace(options.LogStreamName))
            {
                config.LogStreamName = options.LogStreamName;
            }

            var logger = new AWSLoggerCore(config, LogType);
            logger.StartMonitor();
            return logger;
        }

        private static IAmazonCloudWatch CreateMetricsClient(RegionEndpoint? region)
            => region is null ? new AmazonCloudWatchClient() : new AmazonCloudWatchClient(region);

        /// <summary>Unsubscribes from the emitter, flushes remaining metrics, and closes the logger.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _metricTimer?.Dispose();

            if (_emitter is not null && _handler is not null)
            {
                _emitter.OffAll(_handler);
            }

            try
            {
                FlushMetricsAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Best-effort final metric flush.
            }

            // Flushes queued log messages and stops the AWS.Logger.Core background thread.
            _logger.Close();
        }

        // Aggregation key: one metric datum per (metric, agent, policy) group.
        private readonly record struct MetricKey(string Metric, string AgentId, string? PolicyName);
    }
}

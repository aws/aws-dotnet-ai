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
using Amazon.Runtime;
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
        private readonly bool _ownsMetricsClient;

        private AuditEmitter? _emitter;
        private Action<GovernanceEvent>? _handler;
        private int _disposed;

        // Serializes flushes: the timer path enters without blocking (skips its tick if busy), and Dispose
        // blocks on it so the final flush and client disposal can't race an in-flight PutMetricData.
        private readonly SemaphoreSlim _flushGate = new(1, 1);

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
            if (options.FlushInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options), options.FlushInterval, "FlushInterval must be greater than zero.");
            }

            _logger = logger ?? CreateLogger(options);

            if (options.EmitMetrics)
            {
                // Only dispose a client we created; an injected one is owned by the caller.
                _ownsMetricsClient = metricsClient is null;
                _metrics = metricsClient ?? CreateMetricsClient(options.Region, options.Credentials);
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

            try
            {
                // An oversized record is split into multiple independently-valid chunk lines (lossless); the
                // common case is a single line. Enqueue each; AWS.Logger.Core batches them on background threads.
                var lines = GovernanceEventSerializer.Serialize(governanceEvent);
                for (var i = 0; i < lines.Count; i++)
                {
                    _logger.AddMessage(lines[i]);
                }

                if (_metrics is not null)
                {
                    var metric = MapMetricName(governanceEvent.Type);
                    if (metric is not null)
                    {
                        Increment(metric, governanceEvent.AgentId, governanceEvent.PolicyName);
                    }

                    // Diagnostics so operators can see (and alarm on) the rare chunked path without inspecting logs.
                    if (lines.Count > 1)
                    {
                        Increment("AuditRecordsChunked", governanceEvent.AgentId, governanceEvent.PolicyName);
                        if (lines.Count > _options.SoftChunkLimit)
                        {
                            Increment("AuditRecordsExceededSoftLimit", governanceEvent.AgentId, governanceEvent.PolicyName);
                        }
                    }
                }
            }
            catch
            {
                // The audit handler must never throw back into the governance loop. Serialization edge cases,
                // or a logger disposed by a concurrent Dispose(), are swallowed here so a sink hiccup can't
                // break the governed operation. Delivery failures are already handled by AWS.Logger.Core.
            }
        }

        // Bumps an in-memory counter published on the next metric flush. No I/O, never throws.
        private void Increment(string metric, string agentId, string? policyName)
        {
            var key = new MetricKey(metric, agentId, policyName);
            lock (_metricLock)
            {
                _metricCounts[key] = _metricCounts.TryGetValue(key, out var count) ? count + 1 : 1;
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
                // CloudWatch rejects an empty dimension value and fails the whole PutMetricData batch, so fall
                // back to a sentinel when AgentId is empty (required only guarantees it's set, not non-empty).
                var agentId = string.IsNullOrEmpty(pair.Key.AgentId) ? "unknown" : pair.Key.AgentId;
                var dimensions = new List<Dimension> { new() { Name = "AgentId", Value = agentId } };
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
            // A timer callback can still be queued when Dispose() runs; bail before touching the gate/client.
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                // Skip this tick if a flush is already running, so a slow/throttled endpoint can't pile up
                // overlapping PutMetricData calls. The next timer tick picks up whatever accumulated.
                if (!await _flushGate.WaitAsync(0).ConfigureAwait(false))
                {
                    return;
                }

                try
                {
                    await FlushMetricsAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Audit must never break the governance loop. Drop the metric batch on persistent failure.
                }
                finally
                {
                    _flushGate.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Dispose() raced this callback and disposed the gate/client after our _disposed check.
                // Nothing left to flush here; Dispose() runs the final flush under the gate.
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

            if (options.Credentials is not null)
            {
                config.Credentials = options.Credentials;
            }

            if (!string.IsNullOrWhiteSpace(options.LogStreamName))
            {
                config.LogStreamName = options.LogStreamName;
            }

            // AWSLoggerCore's constructor already starts the background delivery monitor. Do NOT call
            // StartMonitor() again here: a second call spins up a second monitor task that races the first
            // over the same in-memory batch/queue, which can drop log events (notably the tail of a burst,
            // e.g. the final chunk of an oversized, chunked audit record).
            return new AWSLoggerCore(config, LogType);
        }

        private static IAmazonCloudWatch CreateMetricsClient(RegionEndpoint? region, AWSCredentials? credentials)
        {
            if (credentials is not null)
            {
                return region is null
                    ? new AmazonCloudWatchClient(credentials)
                    : new AmazonCloudWatchClient(credentials, region);
            }

            return region is null ? new AmazonCloudWatchClient() : new AmazonCloudWatchClient(region);
        }

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
                // Bound shutdown so a hung PutMetricData can't block it indefinitely. Timer.Dispose() does not
                // wait for a fire-and-forget flush it already kicked off, so wait on the gate first: this lets
                // any in-flight timer flush finish before we run (and reset) the final flush and dispose the
                // metrics client, otherwise that client could be disposed mid-request and drop the last batch.
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _flushGate.Wait(cts.Token);
                try
                {
                    FlushMetricsAsync(cts.Token).GetAwaiter().GetResult();
                }
                finally
                {
                    _flushGate.Release();
                }
            }
            catch
            {
                // Best-effort final metric flush.
            }

            if (_ownsMetricsClient)
            {
                _metrics?.Dispose();
            }

            _flushGate.Dispose();

            // Flushes queued log messages and stops the AWS.Logger.Core background thread.
            _logger.Close();
        }

        // Aggregation key: one metric datum per (metric, agent, policy) group.
        private readonly record struct MetricKey(string Metric, string AgentId, string? PolicyName);
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using AWS.Logger.Core;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Audit;
using Moq;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Audit
{
    public class CloudWatchAuditSinkTests
    {
        // A long flush interval keeps the background metric timer out of the way; tests flush explicitly.
        private static CloudWatchAuditOptions Options(bool emitMetrics = false) => new()
        {
            LogGroupName = "/test/audit",
            EmitMetrics = emitMetrics,
            FlushInterval = TimeSpan.FromMinutes(10)
        };

        private static Mock<IAmazonCloudWatch> Metrics()
        {
            var metrics = new Mock<IAmazonCloudWatch>();
            metrics.Setup(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutMetricDataResponse());
            return metrics;
        }

        private static GovernanceEvent Event(GovernanceEventType type = GovernanceEventType.PolicyViolation, string? policy = "default.yaml") => new()
        {
            Type = type,
            AgentId = "did:mesh:agent",
            SessionId = "session-1",
            PolicyName = policy
        };

        [Fact]
        public void OnEvent_writes_serialized_message_to_the_logger()
        {
            var logger = new Mock<IAWSLoggerCore>();
            string? captured = null;
            logger.Setup(l => l.AddMessage(It.IsAny<string>())).Callback<string>(m => captured = m);

            using var sink = new CloudWatchAuditSink(Options(), logger.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            emitter.Emit(Event());

            logger.Verify(l => l.AddMessage(It.IsAny<string>()), Times.Once);
            Assert.NotNull(captured);
            Assert.Contains("PolicyViolation", captured!);
        }

        [Fact]
        public void OnEvent_does_not_publish_metrics_immediately()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();

            using var sink = new CloudWatchAuditSink(Options(emitMetrics: true), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            emitter.Emit(Event());

            // Metrics are aggregated in memory; nothing hits CloudWatch until a flush.
            metrics.Verify(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task FlushMetricsAsync_aggregates_counts_and_omits_policy_dimension_when_null()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();
            PutMetricDataRequest? captured = null;
            metrics.Setup(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutMetricDataRequest, CancellationToken>((r, _) => captured = r)
                .ReturnsAsync(new PutMetricDataResponse());

            using var sink = new CloudWatchAuditSink(Options(emitMetrics: true), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            emitter.Emit(Event(GovernanceEventType.PolicyViolation, "default.yaml"));
            emitter.Emit(Event(GovernanceEventType.PolicyViolation, "default.yaml"));
            emitter.Emit(Event(GovernanceEventType.ToolCallBlocked, policy: null));

            await sink.FlushMetricsAsync();

            Assert.NotNull(captured);
            Assert.Equal("AgentGovernance/Bedrock", captured!.Namespace);
            var violations = Assert.Single(captured.MetricData, d => d.MetricName == "PolicyViolations");
            Assert.Equal(2d, violations.Value.GetValueOrDefault());
            Assert.Contains(violations.Dimensions, dim => dim.Name == "PolicyName");

            var blocked = Assert.Single(captured.MetricData, d => d.MetricName == "ToolCallsBlocked");
            Assert.DoesNotContain(blocked.Dimensions, dim => dim.Name == "PolicyName");
        }

        [Fact]
        public async Task FlushMetricsAsync_resets_counts_so_a_second_flush_is_empty()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();

            using var sink = new CloudWatchAuditSink(Options(emitMetrics: true), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);
            emitter.Emit(Event());

            await sink.FlushMetricsAsync();
            await sink.FlushMetricsAsync();

            // First flush publishes; the second has nothing to publish.
            metrics.Verify(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task FlushMetricsAsync_is_noop_when_metrics_disabled()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();

            using var sink = new CloudWatchAuditSink(Options(emitMetrics: false), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);
            emitter.Emit(Event());

            await sink.FlushMetricsAsync();

            metrics.Verify(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Dispose_closes_the_logger()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var sink = new CloudWatchAuditSink(Options(), logger.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            sink.Dispose();

            logger.Verify(l => l.Close(), Times.Once);
        }

        [Fact]
        public void Dispose_flushes_pending_metrics_exactly_once()
        {
            // The flush interval is 10 minutes, so the timer never fires here: the single PutMetricData proves
            // Dispose runs the final flush (coordinated through the flush gate) before closing down.
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();
            var sink = new CloudWatchAuditSink(Options(emitMetrics: true), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);
            emitter.Emit(Event(GovernanceEventType.PolicyViolation));

            sink.Dispose();

            metrics.Verify(
                c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            // A caller-owned metrics client must not be disposed by the sink.
            metrics.Verify(c => c.Dispose(), Times.Never);
        }

        [Fact]
        public void Dispose_unsubscribes_from_the_emitter()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var sink = new CloudWatchAuditSink(Options(), logger.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            sink.Dispose();
            emitter.Emit(Event());

            // After dispose, further events are not forwarded to the logger.
            logger.Verify(l => l.AddMessage(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void OnEvent_emits_multiple_messages_for_an_oversized_record()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var captured = new List<string>();
            logger.Setup(l => l.AddMessage(It.IsAny<string>())).Callback<string>(m => captured.Add(m));

            using var sink = new CloudWatchAuditSink(Options(), logger.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            emitter.Emit(new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["blob"] = new string('D', 300_000) }
            });

            // The oversized record is enqueued as several chunk lines, not one truncated line.
            Assert.True(captured.Count > 1);
            logger.Verify(l => l.AddMessage(It.IsAny<string>()), Times.Exactly(captured.Count));
        }

        [Fact]
        public async Task Oversized_record_publishes_the_chunked_diagnostic_metric()
        {
            var logger = new Mock<IAWSLoggerCore>();
            var metrics = Metrics();
            PutMetricDataRequest? captured = null;
            metrics.Setup(c => c.PutMetricDataAsync(It.IsAny<PutMetricDataRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PutMetricDataRequest, CancellationToken>((r, _) => captured = r)
                .ReturnsAsync(new PutMetricDataResponse());

            using var sink = new CloudWatchAuditSink(Options(emitMetrics: true), logger.Object, metrics.Object);
            var emitter = new AuditEmitter();
            sink.Subscribe(emitter);

            emitter.Emit(new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["blob"] = new string('D', 300_000) }
            });

            await sink.FlushMetricsAsync();

            Assert.NotNull(captured);
            Assert.Contains(captured!.MetricData, d => d.MetricName == "AuditRecordsChunked");
        }
    }
}

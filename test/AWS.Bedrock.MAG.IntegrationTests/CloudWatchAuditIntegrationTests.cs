// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Audit;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>Writes a governance event to a real CloudWatch log group and reads it back (PR: audit sink).</summary>
    [Collection("bedrock-integration")]
    public class CloudWatchAuditIntegrationTests
    {
        private readonly GuardrailFixture _fx;

        public CloudWatchAuditIntegrationTests(GuardrailFixture fx) => _fx = fx;

        [Fact]
        public async Task Delivers_a_governance_event_to_cloudwatch_logs()
        {
            var eventId = $"evt-int-{Guid.NewGuid():N}";
            var options = new CloudWatchAuditOptions
            {
                LogGroupName = _fx.LogGroupName,
                EmitMetrics = false,
                Region = _fx.Region,
                FlushInterval = TimeSpan.FromSeconds(1)
            };

            using (var sink = new CloudWatchAuditSink(options))
            {
                var emitter = new AuditEmitter();
                sink.Subscribe(emitter);
                emitter.Emit(new GovernanceEvent
                {
                    Type = GovernanceEventType.PolicyViolation,
                    AgentId = "did:mesh:integration",
                    SessionId = "integration-session",
                    PolicyName = "integration",
                    EventId = eventId
                });
                // Dispose (below) flushes and closes the AWS.Logger.Core logger.
            }

            using var logs = new AmazonCloudWatchLogsClient(_fx.Region);
            var found = await WaitForLogAsync(logs, _fx.LogGroupName, eventId, TimeSpan.FromSeconds(90));

            Assert.True(found, $"event {eventId} did not appear in {_fx.LogGroupName} within the timeout");
        }

        [Fact]
        public async Task Delivers_an_oversized_governance_event_as_reassemblable_chunks()
        {
            var eventId = $"evt-int-big-{Guid.NewGuid():N}";
            var blob = new string('D', 2_000_000); // ~2 MB -> multiple ~1 MB chunk lines
            var options = new CloudWatchAuditOptions
            {
                LogGroupName = _fx.LogGroupName,
                EmitMetrics = false,
                Region = _fx.Region,
                FlushInterval = TimeSpan.FromSeconds(1)
            };

            using (var sink = new CloudWatchAuditSink(options))
            {
                var emitter = new AuditEmitter();
                sink.Subscribe(emitter);
                emitter.Emit(new GovernanceEvent
                {
                    Type = GovernanceEventType.PolicyViolation,
                    AgentId = "did:mesh:integration",
                    SessionId = "integration-session",
                    PolicyName = "integration",
                    EventId = eventId,
                    Data = new Dictionary<string, object> { ["blob"] = blob }
                });
                // Dispose flushes and closes the AWS.Logger.Core logger.
            }

            using var logs = new AmazonCloudWatchLogsClient(_fx.Region);
            var reassembled = await WaitForReassembledAsync(logs, _fx.LogGroupName, eventId, TimeSpan.FromSeconds(120));

            Assert.NotNull(reassembled);
            // The full payload survives the CloudWatch round-trip losslessly, across multiple log events.
            using var doc = JsonDocument.Parse(reassembled!);
            Assert.Equal(eventId, doc.RootElement.GetProperty("eventId").GetString());
            Assert.Equal(blob, doc.RootElement.GetProperty("data").GetProperty("blob").GetString());
        }

        // Polls the log group, collecting every event message that carries the eventId (paging as needed), and
        // returns the reassembled record once all of its chunks have arrived.
        private static async Task<string?> WaitForReassembledAsync(IAmazonCloudWatchLogs logs, string logGroup, string eventId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var lines = new List<string>();
                string? nextToken = null;
                try
                {
                    do
                    {
                        var response = await logs.FilterLogEventsAsync(new FilterLogEventsRequest
                        {
                            LogGroupName = logGroup,
                            NextToken = nextToken
                        });

                        if (response.Events is not null)
                        {
                            lines.AddRange(response.Events
                                .Where(e => e.Message is not null && e.Message.Contains(eventId, StringComparison.Ordinal))
                                .Select(e => e.Message!));
                        }

                        nextToken = response.NextToken;
                    }
                    while (!string.IsNullOrEmpty(nextToken));
                }
                catch (ResourceNotFoundException)
                {
                    // Log group/stream not created by AWS.Logger.Core yet.
                }

                var record = GovernanceAuditReader.Reassemble(lines)
                    .FirstOrDefault(r => r.EventId == eventId && r.IsComplete);
                if (record is not null)
                {
                    return record.Json;
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            return null;
        }

        private static async Task<bool> WaitForLogAsync(IAmazonCloudWatchLogs logs, string logGroup, string needle, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var response = await logs.FilterLogEventsAsync(new FilterLogEventsRequest { LogGroupName = logGroup });
                    if (response.Events is not null && response.Events.Any(e => e.Message is not null && e.Message.Contains(needle, StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
                catch (ResourceNotFoundException)
                {
                    // Log group/stream not created by AWS.Logger.Core yet.
                }

                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            return false;
        }
    }
}

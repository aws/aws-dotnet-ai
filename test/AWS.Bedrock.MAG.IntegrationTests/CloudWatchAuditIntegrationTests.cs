// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG;
using AWS.Bedrock.MAG.Audit;
using AWS.Bedrock.MAG.IntegrationTests.Infrastructure;
using AWS.Bedrock.MAG.Policy;
using Xunit;

namespace AWS.Bedrock.MAG.IntegrationTests
{
    /// <summary>Writes a governance event to a real CloudWatch log group and reads it back (PR: audit sink).</summary>
    [Collection("bedrock-integration")]
    public class CloudWatchAuditIntegrationTests
    {
        private readonly GuardrailFixture _fx;
        private readonly ITestOutputHelper _output;

        public CloudWatchAuditIntegrationTests(GuardrailFixture fx, ITestOutputHelper output)
        {
            _fx = fx;
            _output = output;
        }

        [Fact]
        public async Task Delivers_a_governance_event_to_cloudwatch_logs()
        {
            var eventId = $"evt-int-{Guid.NewGuid():N}";
            var start = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
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
            var found = await WaitForLogAsync(logs, _fx.LogGroupName, eventId, start, TimeSpan.FromSeconds(90));

            Assert.True(found, $"event {eventId} did not appear in {_fx.LogGroupName} within the timeout");
        }

        [Fact]
        public async Task Delivers_an_oversized_governance_event_as_reassemblable_chunks()
        {
            var eventId = $"evt-int-big-{Guid.NewGuid():N}";
            var blob = new string('D', 300_000); // ~300 KB -> multiple chunk lines over the 256 KB limit
            var start = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
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
            var reassembled = await WaitForReassembledAsync(logs, _fx.LogGroupName, eventId, start, TimeSpan.FromSeconds(120));

            Assert.NotNull(reassembled);
            // The full payload survives the CloudWatch round-trip losslessly, across multiple log events.
            using var doc = JsonDocument.Parse(reassembled!);
            Assert.Equal(eventId, doc.RootElement.GetProperty("eventId").GetString());
            Assert.Equal(blob, doc.RootElement.GetProperty("data").GetProperty("blob").GetString());
        }

        // End-to-end proof for plain-text code-point-boundary chunking: a REAL Bedrock guardrail evaluation
        // feeds an oversized governance record whose payload is multi-byte UTF-8 (emoji, CJK, accents). It is
        // written through the real sink (which splits it into several readable plain-text chunk lines on code-
        // point boundaries) and read back — and every code point must survive byte-for-byte. The local contrast
        // at the end shows a naive raw-byte slice corrupting the SAME bytes, versus a code-point-boundary slice
        // (what the sink does) reconstructing them exactly, with no base64.
        [Fact]
        public async Task Round_trips_oversized_multibyte_utf8_from_a_real_bedrock_evaluation()
        {
            // "🎉" is 4 UTF-8 bytes (a UTF-16 surrogate pair), "私" is 3, "é" is 2, "a" is 1 — a mix so a naive
            // byte cut would overwhelmingly likely fall inside a multi-byte sequence.
            const string unit = "🎉私éa";

            // 1) A real Bedrock guardrail evaluation over a multi-byte prompt. Proves multi-byte text survives
            //    the Bedrock request/response round-trip before it ever reaches the audit sink.
            var prompt = string.Concat(Enumerable.Repeat(unit, 64)); // benign; just multi-byte, not the block word
            var backend = new BedrockGuardrailsPolicyBackend(
                new BedrockGuardrailsPolicyOptions { GuardrailId = _fx.GuardrailId, Region = _fx.Region });
            var decision = await backend.EvaluateAsync(new Dictionary<string, object>
            {
                ["tool"] = "echo",
                ["prompt"] = prompt
            });
            Assert.Equal("bedrock-guardrails", decision.Backend);

            // 2) Build an oversized (~300 KB > the 256 KB library limit) multi-byte payload so the record MUST
            //    be split into several plain-text chunk lines, exercising the code-point-boundary split.
            var bigMultiByte = string.Concat(Enumerable.Repeat(unit, 30_000)); // 30k * 10 bytes ≈ 300 KB UTF-8
            var eventId = $"evt-int-mbutf8-{Guid.NewGuid():N}";
            var start = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
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
                    Data = new Dictionary<string, object>
                    {
                        // Real decision fields alongside the multi-byte content that must survive chunking.
                        ["allowed"] = decision.Allowed,
                        ["backend"] = decision.Backend,
                        ["prompt"] = prompt,
                        ["blob"] = bigMultiByte
                    }
                });
                // Dispose flushes and closes the AWS.Logger.Core logger.
            }

            using var logs = new AmazonCloudWatchLogsClient(_fx.Region);
            var reassembled = await WaitForReassembledAsync(logs, _fx.LogGroupName, eventId, start, TimeSpan.FromSeconds(120));

            Assert.NotNull(reassembled);
            using var doc = JsonDocument.Parse(reassembled!);
            Assert.Equal(eventId, doc.RootElement.GetProperty("eventId").GetString());
            var data = doc.RootElement.GetProperty("data");
            // The multi-byte content is reproduced exactly, across the CloudWatch chunk round-trip.
            Assert.Equal(bigMultiByte, data.GetProperty("blob").GetString());
            Assert.Equal(prompt, data.GetProperty("prompt").GetString());
            _output.WriteLine($"Chunked multi-byte record round-tripped losslessly: {bigMultiByte.Length} chars / {Encoding.UTF8.GetByteCount(bigMultiByte)} UTF-8 bytes.");

            // 3) Local contrast (no AWS): the SAME payload bytes sliced two ways. A naive byte cut lands inside a
            //    multi-byte character and corrupts it (each half decodes to U+FFFD); a code-point-boundary cut —
            //    what the sink does — reconstructs the text exactly, as plain readable text with no base64.
            var bytes = Encoding.UTF8.GetBytes(bigMultiByte);
            var midChar = FindMidCharacterBoundary(bytes);

            // Naive path: decode each raw-byte half as UTF-8 and concatenate. The split sequence becomes U+FFFD.
            var naive = Encoding.UTF8.GetString(bytes[..midChar]) + Encoding.UTF8.GetString(bytes[midChar..]);
            Assert.NotEqual(bigMultiByte, naive);
            Assert.Contains('�', naive); // the replacement character proves a code point was destroyed

            // Safe path: back up to the code-point boundary before cutting; both halves are valid UTF-8 and the
            // concatenation is exact.
            var boundary = midChar;
            while (boundary > 0 && (bytes[boundary] & 0xC0) == 0x80)
            {
                boundary--;
            }

            var safe = Encoding.UTF8.GetString(bytes[..boundary]) + Encoding.UTF8.GetString(bytes[boundary..]);
            Assert.Equal(bigMultiByte, safe);
            Assert.DoesNotContain('�', safe);
            _output.WriteLine($"Raw slice at byte {midChar} corrupts (U+FFFD); code-point-boundary slice at {boundary} is exact.");
        }

        // Returns a byte index that lands strictly inside a multi-byte UTF-8 sequence, i.e. where the byte at
        // the index is a continuation byte (0b10xxxxxx). Slicing there splits a code point in half.
        private static int FindMidCharacterBoundary(byte[] utf8)
        {
            for (var i = utf8.Length / 2; i < utf8.Length; i++)
            {
                if ((utf8[i] & 0xC0) == 0x80)
                {
                    return i;
                }
            }

            throw new System.InvalidOperationException("No multi-byte boundary found; payload was not multi-byte UTF-8.");
        }

        // Polls the log group, collecting every event message that carries the eventId (paging as needed), and
        // returns the reassembled record once all of its chunks have arrived.
        private static async Task<string?> WaitForReassembledAsync(IAmazonCloudWatchLogs logs, string logGroup, string eventId, long startTimeMs, TimeSpan timeout)
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
                            // Scope the scan server-side: only events since the test started that carry this
                            // eventId, instead of reading the whole group and filtering client-side.
                            StartTime = startTimeMs,
                            FilterPattern = $"\"{eventId}\"",
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

        private static async Task<bool> WaitForLogAsync(IAmazonCloudWatchLogs logs, string logGroup, string needle, long startTimeMs, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var response = await logs.FilterLogEventsAsync(new FilterLogEventsRequest
                    {
                        LogGroupName = logGroup,
                        StartTime = startTimeMs,
                        FilterPattern = $"\"{needle}\""
                    });
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

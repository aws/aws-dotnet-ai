// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG.Audit;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Audit
{
    public class GovernanceEventSerializerTests
    {
        // The common case emits exactly one line; unwrap it and assert that invariant.
        private static string SerializeSingle(GovernanceEvent e)
        {
            var lines = GovernanceEventSerializer.Serialize(e);
            return Assert.Single(lines);
        }

        [Fact]
        public void Serialize_emits_core_fields()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                PolicyName = "default.yaml"
            };

            var json = SerializeSingle(e);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("PolicyViolation", root.GetProperty("type").GetString());
            Assert.Equal("did:mesh:agent", root.GetProperty("agentId").GetString());
            Assert.Equal("session-1", root.GetProperty("sessionId").GetString());
            Assert.Equal("default.yaml", root.GetProperty("policyName").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("eventId").GetString()));
            Assert.False(string.IsNullOrEmpty(root.GetProperty("timestamp").GetString()));
        }

        [Fact]
        public void Serialize_omits_policy_name_when_null()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyCheck,
                AgentId = "did:mesh:agent",
                SessionId = "session-1"
            };

            var json = SerializeSingle(e);

            using var doc = JsonDocument.Parse(json);
            Assert.False(doc.RootElement.TryGetProperty("policyName", out _));
        }

        [Fact]
        public void Serialize_writes_data_with_typed_values()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object>
                {
                    ["kind"] = "pii_redaction",
                    ["count"] = 3,
                    ["blocked"] = true
                }
            };

            var json = SerializeSingle(e);

            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            Assert.Equal("pii_redaction", data.GetProperty("kind").GetString());
            Assert.Equal(3, data.GetProperty("count").GetInt32());
            Assert.True(data.GetProperty("blocked").GetBoolean());
        }

        [Fact]
        public void Serialize_does_not_throw_on_non_finite_numbers()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyCheck,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["latency"] = double.NaN, ["ratio"] = double.PositiveInfinity }
            };

            // Must produce valid JSON rather than throwing (which would drop the whole audit batch).
            var json = SerializeSingle(e);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("data", out _));
        }

        [Fact]
        public void Serialize_formats_datetime_values_invariantly()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyCheck,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["at"] = new System.DateTime(2026, 8, 17, 13, 5, 0, System.DateTimeKind.Utc) }
            };

            var json = SerializeSingle(e);

            using var doc = JsonDocument.Parse(json);
            var at = doc.RootElement.GetProperty("data").GetProperty("at").GetString();
            // Round-trippable ISO-8601 with the UTC marker (format "O"), not the lossy general "G" form. Parsing
            // it back must recover the exact UTC instant, not an Unspecified-kind local time.
            var parsed = System.DateTime.Parse(at!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);
            Assert.Equal(System.DateTimeKind.Utc, parsed.Kind);
            Assert.Equal(new System.DateTime(2026, 8, 17, 13, 5, 0, System.DateTimeKind.Utc), parsed.ToUniversalTime());
        }

        [Fact]
        public void Serialize_writes_nested_dictionary_data_as_a_json_object()
        {
            // GovernanceMiddleware stores tool arguments as Data["arguments"] = Dictionary<string, object>.
            // These must survive as a nested JSON object (with PII visible) rather than the dictionary type name.
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyCheck,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object>
                {
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["ssn"] = "123-45-6789",
                        ["nested"] = new Dictionary<string, string> { ["email"] = "a@b.com" },
                        ["tags"] = new[] { "x", "y" }
                    }
                }
            };

            var json = SerializeSingle(e);

            using var doc = JsonDocument.Parse(json);
            var args = doc.RootElement.GetProperty("data").GetProperty("arguments");
            Assert.Equal(JsonValueKind.Object, args.ValueKind);
            Assert.Equal("123-45-6789", args.GetProperty("ssn").GetString());
            Assert.Equal("a@b.com", args.GetProperty("nested").GetProperty("email").GetString());
            Assert.Equal(2, args.GetProperty("tags").GetArrayLength());
        }

        [Fact]
        public void Serialize_splits_oversized_record_into_multiple_valid_chunk_lines()
        {
            var e = OversizedEvent(out _);

            var lines = GovernanceEventSerializer.Serialize(e);

            Assert.True(lines.Count > 1, "an over-cap record should span multiple chunk lines");
            for (var i = 0; i < lines.Count; i++)
            {
                // Each line is independently valid JSON and stays under the CloudWatch event cap.
                using var doc = JsonDocument.Parse(lines[i]);
                var root = doc.RootElement;
                Assert.Equal("did:mesh:agent", root.GetProperty("agentId").GetString());
                var chunk = root.GetProperty("chunk");
                Assert.Equal(i, chunk.GetProperty("i").GetInt32());
                Assert.Equal(lines.Count, chunk.GetProperty("n").GetInt32());
                Assert.Equal("base64", chunk.GetProperty("enc").GetString());
                Assert.True(root.TryGetProperty("payload", out _));
                Assert.True(Encoding.UTF8.GetByteCount(lines[i]) <= 256_000);
            }
        }

        [Fact]
        public void Chunked_record_reassembles_byte_identical_to_the_unchunked_serialization()
        {
            var e = OversizedEvent(out var blob);
            var lines = GovernanceEventSerializer.Serialize(e);
            Assert.True(lines.Count > 1);

            var record = Assert.Single(GovernanceAuditReader.Reassemble(lines));
            Assert.True(record.IsComplete);
            Assert.NotNull(record.Json);

            // The reassembled record is exactly what a single-line serialization would have produced, so the
            // full governance payload (including the large blob) is recovered losslessly.
            using var doc = JsonDocument.Parse(record.Json!);
            Assert.Equal(e.EventId, doc.RootElement.GetProperty("eventId").GetString());
            Assert.Equal(blob, doc.RootElement.GetProperty("data").GetProperty("blob").GetString());
        }

        [Fact]
        public void Chunking_preserves_multibyte_utf8_data()
        {
            // Emoji/CJK exercise the base64 path's immunity to multi-byte boundary splits.
            var unit = "🌍你好-café ";
            var big = string.Concat(Enumerable.Repeat(unit, 200_000)); // well over the 1 MB cap
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["blob"] = big }
            };

            var lines = GovernanceEventSerializer.Serialize(e);
            Assert.True(lines.Count > 1);

            var record = Assert.Single(GovernanceAuditReader.Reassemble(lines));
            Assert.True(record.IsComplete);
            using var doc = JsonDocument.Parse(record.Json!);
            Assert.Equal(big, doc.RootElement.GetProperty("data").GetProperty("blob").GetString());
        }

        [Fact]
        public void Small_record_passes_through_the_reader_unchanged()
        {
            var e = new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyCheck,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                Data = new Dictionary<string, object> { ["kind"] = "ok" }
            };
            var lines = GovernanceEventSerializer.Serialize(e);

            var record = Assert.Single(GovernanceAuditReader.Reassemble(lines));
            Assert.True(record.IsComplete);
            Assert.Equal(lines[0], record.Json);
        }

        [Fact]
        public void Reader_reports_incomplete_when_a_chunk_is_missing()
        {
            var e = OversizedEvent(out _);
            var lines = GovernanceEventSerializer.Serialize(e).ToList();
            Assert.True(lines.Count > 1);

            lines.RemoveAt(1); // drop one chunk

            var record = Assert.Single(GovernanceAuditReader.Reassemble(lines));
            Assert.False(record.IsComplete);
            Assert.Null(record.Json);
            Assert.Contains(1, record.MissingIndices);
        }

        [Fact]
        public void Reader_reassembles_multiple_interleaved_records()
        {
            var a = OversizedEvent(out _);
            var b = OversizedEvent(out _);
            var linesA = GovernanceEventSerializer.Serialize(a);
            var linesB = GovernanceEventSerializer.Serialize(b);

            // Interleave the two records' chunks to prove reassembly is keyed on eventId + index, not order.
            var mixed = linesA.Zip(linesB, (x, y) => new[] { x, y }).SelectMany(p => p).ToList();
            if (linesA.Count > linesB.Count) mixed.AddRange(linesA.Skip(linesB.Count));
            if (linesB.Count > linesA.Count) mixed.AddRange(linesB.Skip(linesA.Count));

            var records = GovernanceAuditReader.Reassemble(mixed).ToList();
            Assert.Equal(2, records.Count);
            Assert.All(records, r => Assert.True(r.IsComplete));
            Assert.Contains(records, r => r.EventId == a.EventId);
            Assert.Contains(records, r => r.EventId == b.EventId);
        }

        private static GovernanceEvent OversizedEvent(out string blob)
        {
            blob = new string('D', 2_000_000); // ~2 MB payload -> multiple ~1 MB chunks
            return new GovernanceEvent
            {
                Type = GovernanceEventType.PolicyViolation,
                AgentId = "did:mesh:agent",
                SessionId = "session-1",
                PolicyName = "big-policy",
                Data = new Dictionary<string, object> { ["blob"] = blob }
            };
        }
    }
}

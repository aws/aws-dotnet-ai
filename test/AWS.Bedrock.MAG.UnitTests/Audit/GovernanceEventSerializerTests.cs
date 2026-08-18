// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Text.Json;
using AgentGovernance.Audit;
using AWS.Bedrock.MAG.Audit;
using Xunit;

namespace AWS.Bedrock.MAG.UnitTests.Audit
{
    public class GovernanceEventSerializerTests
    {
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

            var json = GovernanceEventSerializer.Serialize(e);

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

            var json = GovernanceEventSerializer.Serialize(e);

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

            var json = GovernanceEventSerializer.Serialize(e);

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
            var json = GovernanceEventSerializer.Serialize(e);
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

            var json = GovernanceEventSerializer.Serialize(e);

            using var doc = JsonDocument.Parse(json);
            var at = doc.RootElement.GetProperty("data").GetProperty("at").GetString();
            Assert.Contains("2026", at);
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentGovernance.Audit;

namespace AWS.Bedrock.MAG.Audit
{
    /// <summary>
    /// Serializes a <see cref="GovernanceEvent"/> to a compact, structured JSON line for CloudWatch Logs.
    /// Written with <see cref="Utf8JsonWriter"/> (no reflection) so it stays AOT and trimming safe.
    /// </summary>
    internal static class GovernanceEventSerializer
    {
        public static string Serialize(GovernanceEvent e)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("type", e.Type.ToString());
                writer.WriteString("timestamp", e.Timestamp.ToString("O"));
                writer.WriteString("agentId", e.AgentId);
                writer.WriteString("sessionId", e.SessionId);
                if (e.PolicyName is not null)
                {
                    writer.WriteString("policyName", e.PolicyName);
                }

                writer.WriteString("eventId", e.EventId);

                if (e.Data is { Count: > 0 })
                {
                    writer.WritePropertyName("data");
                    writer.WriteStartObject();
                    foreach (var pair in e.Data)
                    {
                        writer.WritePropertyName(pair.Key);
                        WriteValue(writer, pair.Value);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    // Utf8JsonWriter throws on NaN/Infinity; emit as a string so one bad value can't drop the batch.
                    if (double.IsFinite(d)) { writer.WriteNumberValue(d); } else { writer.WriteStringValue(d.ToString(CultureInfo.InvariantCulture)); }
                    break;
                case float f:
                    if (float.IsFinite(f)) { writer.WriteNumberValue(f); } else { writer.WriteStringValue(f.ToString(CultureInfo.InvariantCulture)); }
                    break;
                case decimal m:
                    writer.WriteNumberValue(m);
                    break;
                case IFormattable formattable:
                    // Invariant, round-trippable form for DateTime/DateTimeOffset/TimeSpan/Guid/etc.
                    writer.WriteStringValue(SafeToString(() => formattable.ToString(null, CultureInfo.InvariantCulture)));
                    break;
                default:
                    writer.WriteStringValue(SafeToString(value.ToString));
                    break;
            }
        }

        // A custom Data value whose ToString/IFormattable throws must not drop the whole audit record.
        private static string SafeToString(Func<string?> toString)
        {
            try
            {
                return toString() ?? string.Empty;
            }
            catch
            {
                return "<unserializable>";
            }
        }
    }
}

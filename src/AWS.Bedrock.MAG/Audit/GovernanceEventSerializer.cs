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
        // CloudWatch Logs caps a single event near 256 KB, and AWS.Logger.Core splits an over-cap message into
        // raw substrings that are each no longer valid JSON. Stay conservatively under that so every emitted
        // line remains one valid JSON object; an over-cap record is replaced by a compact envelope below.
        private const int MaxMessageBytes = 256_000;

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
                        WriteValue(writer, pair.Value, 0);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            // ArrayBufferWriter<byte>.WrittenCount is the exact UTF-8 byte length.
            if (buffer.WrittenCount <= MaxMessageBytes)
            {
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }

            return SerializeTruncatedEnvelope(e, buffer.WrittenCount);
        }

        // Keeps the routing/identity fields (so the record is still findable and countable) but drops the
        // oversized Data, replacing it with a truncation marker. Always well under MaxMessageBytes.
        private static string SerializeTruncatedEnvelope(GovernanceEvent e, int originalBytes)
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
                writer.WriteBoolean("truncated", true);
                writer.WriteNumber("originalBytes", originalBytes);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        // Bounds recursion into caller-supplied Data so pathological nesting can't overflow the stack.
        private const int MaxDepth = 32;

        private static void WriteValue(Utf8JsonWriter writer, object? value, int depth = 0)
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
                // Round-trippable ISO-8601 ("O" preserves the UTC/offset marker and sub-second precision). The
                // general IFormattable path below uses format "G", which drops both, so handle these first.
                case DateTime dt:
                    writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                    break;
                case DateTimeOffset dto:
                    writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                    break;
                // Nested dictionaries/collections (e.g. Data["arguments"] = Dictionary<string, object>) are
                // written structurally rather than stringified to a CLR type name, so tool-call arguments and
                // any PII inside them survive in the audit record. Depth-limited against pathological nesting;
                // the non-generic IDictionary case also covers maps whose value type is not object.
                case System.Collections.IDictionary dict when depth < MaxDepth:
                    writer.WriteStartObject();
                    foreach (System.Collections.DictionaryEntry entry in dict)
                    {
                        writer.WritePropertyName(entry.Key?.ToString() ?? "null");
                        WriteValue(writer, entry.Value, depth + 1);
                    }

                    writer.WriteEndObject();
                    break;
                case System.Collections.IEnumerable seq when depth < MaxDepth:
                    writer.WriteStartArray();
                    foreach (var item in seq)
                    {
                        WriteValue(writer, item, depth + 1);
                    }

                    writer.WriteEndArray();
                    break;
                case IFormattable formattable:
                    // Invariant, round-trippable form for TimeSpan/Guid/etc.
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

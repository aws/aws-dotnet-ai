// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AgentGovernance.Audit;

namespace AWS.Bedrock.MAG.Audit
{
    /// <summary>
    /// Serializes a <see cref="GovernanceEvent"/> to one or more compact, structured JSON lines for
    /// CloudWatch Logs. Written with <see cref="Utf8JsonWriter"/> (no reflection) so it stays AOT and
    /// trimming safe.
    /// <para>
    /// A record that fits under the single-event cap is emitted as exactly one line, byte-for-byte the same
    /// as before. A record that exceeds the cap is split, losslessly, into N independently-valid JSON
    /// "chunk" lines: the full record is base64-encoded and sliced across the lines' <c>payload</c> fields,
    /// each line also carrying the routing/identity fields (so it stays findable in Logs Insights) plus a
    /// <c>chunk</c> descriptor (<c>i</c>, <c>n</c>, <c>len</c>). A consumer reassembles the original record
    /// with <see cref="GovernanceAuditReader"/>. Governance data is never dropped.
    /// </para>
    /// </summary>
    internal static class GovernanceEventSerializer
    {
        // AWS.Logger.Core (4.0.3) splits any single AddMessage whose UTF-8 size exceeds 256,000 bytes into raw
        // substrings that are each no longer valid JSON — regardless of CloudWatch's larger ~1 MB event cap.
        // So both the "when to chunk" threshold AND the per-chunk size are bounded by the LIBRARY's split
        // limit, not the service cap: every emitted line must stay at or under this to survive as one valid
        // JSON event. An over-limit record is split into multiple valid chunk lines below.
        //
        // TODO(aws-logging-dotnet#371): once that PR ships and this package upgrades to the AWS.Logger.Core
        // version that raises the per-message limit to 1 MB, bump this to 1_000_000 (in lockstep with the
        // PackageReference upgrade). Raising it without the matching library version would let the library
        // re-split our chunk lines into invalid JSON again. Fewer/larger chunks then; behavior is otherwise
        // unchanged. https://github.com/aws/aws-logging-dotnet/pull/371
        private const int MaxMessageBytes = 256_000;

        // Headroom subtracted from the per-chunk payload budget so the envelope (identity fields + chunk
        // descriptor) plus the chunk-index/count digits always fit alongside the payload under MaxMessageBytes.
        private const int ChunkEnvelopeHeadroom = 4096;

        // Bounds recursion into caller-supplied Data so pathological nesting can't overflow the stack.
        private const int MaxDepth = 32;

        // Relaxed escaping for chunk lines keeps the base64 alphabet (+ / =) verbatim, so the payload's UTF-8
        // byte length equals its character count and the budget math stays exact. The output is still valid
        // JSON (only ", \\ and control chars must be escaped); JsonDocument reassembles it faithfully.
        private static readonly JsonWriterOptions ChunkWriterOptions =
            new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>
        /// Serializes an event to one JSON line when it fits under the CloudWatch event cap, or to N base64
        /// chunk lines when it does not. The common case returns a single-element list.
        /// </summary>
        public static IReadOnlyList<string> Serialize(GovernanceEvent e)
        {
            var buffer = new ArrayBufferWriter<byte>();
            WriteRecord(buffer, e);

            // ArrayBufferWriter<byte>.WrittenCount is the exact UTF-8 byte length.
            if (buffer.WrittenCount <= MaxMessageBytes)
            {
                return new[] { Encoding.UTF8.GetString(buffer.WrittenSpan) };
            }

            return Chunk(e, buffer.WrittenSpan);
        }

        // Writes the full record: identity fields followed by the optional Data object. Uses the default
        // encoder so single-line output is byte-identical to previous behavior.
        private static void WriteRecord(IBufferWriter<byte> buffer, GovernanceEvent e)
        {
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            WriteIdentity(writer, e);

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

        // Splits an over-cap record losslessly across N chunk lines. The base64 of the ENTIRE record is sliced
        // across payloads; concatenating the payloads in index order and base64-decoding reproduces the exact
        // original record bytes. Never truncates: N grows as large as the record requires.
        private static IReadOnlyList<string> Chunk(GovernanceEvent e, ReadOnlySpan<byte> record)
        {
            var recordLength = record.Length;
            var base64 = Convert.ToBase64String(record);

            // Measure this event's envelope (identity + chunk descriptor, empty payload) so the payload budget
            // accounts for caller-supplied identity fields of any size. Index/count digit growth is covered by
            // ChunkEnvelopeHeadroom.
            var overhead = MeasureChunkEnvelopeBytes(e, recordLength);
            var budget = MaxMessageBytes - overhead - ChunkEnvelopeHeadroom;

            if (budget <= 0)
            {
                // Pathological: the identity fields alone approach the cap (not reachable with normal ids). Emit
                // a single marker line rather than an invalid or oversized one; still no silent data drop.
                return new[] { WriteChunkingFailedMarker(e, recordLength) };
            }

            // Payload is base64 (ASCII), so one character is one UTF-8 byte: slice by character count == bytes.
            var chunkCount = (base64.Length + budget - 1) / budget;
            var lines = new List<string>(chunkCount);
            for (var i = 0; i < chunkCount; i++)
            {
                var start = i * budget;
                var take = Math.Min(budget, base64.Length - start);
                lines.Add(WriteChunkLine(e, i, chunkCount, recordLength, base64.AsSpan(start, take)));
            }

            return lines;
        }

        private static int MeasureChunkEnvelopeBytes(GovernanceEvent e, int recordLength)
        {
            var buffer = new ArrayBufferWriter<byte>();
            WriteChunkLine(buffer, e, index: 0, count: 0, recordLength, ReadOnlySpan<char>.Empty);
            return buffer.WrittenCount;
        }

        private static string WriteChunkLine(GovernanceEvent e, int index, int count, int recordLength, ReadOnlySpan<char> payload)
        {
            var buffer = new ArrayBufferWriter<byte>();
            WriteChunkLine(buffer, e, index, count, recordLength, payload);
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static void WriteChunkLine(IBufferWriter<byte> buffer, GovernanceEvent e, int index, int count, int recordLength, ReadOnlySpan<char> payload)
        {
            using var writer = new Utf8JsonWriter(buffer, ChunkWriterOptions);
            writer.WriteStartObject();
            WriteIdentity(writer, e);

            writer.WritePropertyName("chunk");
            writer.WriteStartObject();
            writer.WriteNumber("v", 1);                 // envelope schema version
            writer.WriteNumber("i", index);             // zero-based chunk index
            writer.WriteNumber("n", count);             // total chunk count
            writer.WriteString("enc", "base64");        // payload encoding
            writer.WriteNumber("len", recordLength);    // total decoded record byte length (integrity check)
            writer.WriteEndObject();

            writer.WriteString("payload", payload);
            writer.WriteEndObject();
        }

        // Keeps the routing/identity fields so a record that cannot be chunked (identity fields alone near the
        // cap) is still findable and flagged, rather than dropped silently or emitted as invalid JSON.
        private static string WriteChunkingFailedMarker(GovernanceEvent e, int originalBytes)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, ChunkWriterOptions))
            {
                writer.WriteStartObject();
                WriteIdentity(writer, e);
                writer.WriteBoolean("chunkingFailed", true);
                writer.WriteNumber("originalBytes", originalBytes);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        // The identity/routing fields shared by full-record and chunk lines, in a stable order.
        private static void WriteIdentity(Utf8JsonWriter writer, GovernanceEvent e)
        {
            writer.WriteString("type", e.Type.ToString());
            writer.WriteString("timestamp", e.Timestamp.ToString("O"));
            writer.WriteString("agentId", e.AgentId);
            writer.WriteString("sessionId", e.SessionId);
            if (e.PolicyName is not null)
            {
                writer.WriteString("policyName", e.PolicyName);
            }

            writer.WriteString("eventId", e.EventId);
        }

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

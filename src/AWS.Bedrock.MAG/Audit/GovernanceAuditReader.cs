// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AWS.Bedrock.MAG.Audit
{
    /// <summary>
    /// Reassembles governance audit records from CloudWatch Logs lines produced by the audit sink. Most
    /// records are written as a single JSON line and pass through unchanged; a record that exceeded the
    /// CloudWatch event cap is split into multiple base64 "chunk" lines, which this reader stitches back into
    /// the original record losslessly.
    /// <para>
    /// Reassembly is by the <c>chunk.i</c> index, never by log order: chunks of one record may share a
    /// timestamp or land in different batches. A record whose chunks are not all present is reported as
    /// incomplete rather than returned as corrupt JSON. Parsing is DOM-based (<see cref="JsonDocument"/>), so
    /// the reader is AOT/trimming safe.
    /// </para>
    /// </summary>
    public static class GovernanceAuditReader
    {
        /// <summary>
        /// Reassembles the given raw log lines into governance records. Unchunked lines are returned as-is;
        /// chunk lines are grouped by <c>eventId</c> and reassembled. Lines that are not valid JSON objects
        /// are ignored (they are not sink output).
        /// </summary>
        /// <param name="logLines">Raw CloudWatch log event messages, in any order.</param>
        /// <returns>One <see cref="ReassembledRecord"/> per distinct record encountered.</returns>
        public static IEnumerable<ReassembledRecord> Reassemble(IEnumerable<string> logLines)
        {
            ArgumentNullException.ThrowIfNull(logLines);

            var passthrough = new List<ReassembledRecord>();
            var groups = new Dictionary<string, ChunkGroup>(StringComparer.Ordinal);

            foreach (var line in logLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue; // Not a sink line; skip.
                }

                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object ||
                        !root.TryGetProperty("eventId", out var eventIdElement) ||
                        eventIdElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var eventId = eventIdElement.GetString()!;

                    if (!TryReadChunk(root, out var chunk))
                    {
                        // Unchunked record: return the line verbatim.
                        passthrough.Add(new ReassembledRecord(eventId, line, true, Array.Empty<int>()));
                        continue;
                    }

                    if (!groups.TryGetValue(eventId, out var group))
                    {
                        group = new ChunkGroup(chunk.Count, chunk.Length);
                        groups[eventId] = group;
                    }

                    group.Add(chunk);
                }
            }

            foreach (var record in passthrough)
            {
                yield return record;
            }

            foreach (var pair in groups)
            {
                yield return pair.Value.Build(pair.Key);
            }
        }

        private static bool TryReadChunk(JsonElement root, out ChunkPart chunk)
        {
            chunk = default;
            if (!root.TryGetProperty("chunk", out var chunkElement) || chunkElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!chunkElement.TryGetProperty("i", out var iElement) || iElement.ValueKind != JsonValueKind.Number ||
                !chunkElement.TryGetProperty("n", out var nElement) || nElement.ValueKind != JsonValueKind.Number ||
                !root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var length = 0;
            if (chunkElement.TryGetProperty("len", out var lenElement) && lenElement.ValueKind == JsonValueKind.Number)
            {
                lenElement.TryGetInt32(out length);
            }

            chunk = new ChunkPart(iElement.GetInt32(), nElement.GetInt32(), length, payloadElement.GetString()!);
            return true;
        }

        private readonly struct ChunkPart
        {
            public ChunkPart(int index, int count, int length, string payload)
            {
                Index = index;
                Count = count;
                Length = length;
                Payload = payload;
            }

            public int Index { get; }
            public int Count { get; }
            public int Length { get; }
            public string Payload { get; }
        }

        private sealed class ChunkGroup
        {
            private readonly Dictionary<int, string> _parts = new();
            private readonly int _count;
            private readonly int _length;

            public ChunkGroup(int count, int length)
            {
                _count = count;
                _length = length;
            }

            public void Add(ChunkPart chunk) => _parts[chunk.Index] = chunk.Payload;

            public ReassembledRecord Build(string eventId)
            {
                var missing = Enumerable.Range(0, _count).Where(i => !_parts.ContainsKey(i)).ToArray();
                if (missing.Length > 0 || _count <= 0)
                {
                    return new ReassembledRecord(eventId, null, false, missing);
                }

                var builder = new StringBuilder();
                for (var i = 0; i < _count; i++)
                {
                    builder.Append(_parts[i]);
                }

                byte[] decoded;
                try
                {
                    decoded = Convert.FromBase64String(builder.ToString());
                }
                catch (FormatException)
                {
                    return new ReassembledRecord(eventId, null, false, Array.Empty<int>());
                }

                // The len field is the original record's byte length; a mismatch means corruption/loss.
                if (_length > 0 && decoded.Length != _length)
                {
                    return new ReassembledRecord(eventId, null, false, Array.Empty<int>());
                }

                return new ReassembledRecord(eventId, Encoding.UTF8.GetString(decoded), true, Array.Empty<int>());
            }
        }
    }

    /// <summary>
    /// A governance audit record recovered by <see cref="GovernanceAuditReader"/>.
    /// </summary>
    /// <param name="EventId">The record's <c>eventId</c>.</param>
    /// <param name="Json">
    /// The reassembled JSON record, or <see langword="null"/> when <paramref name="IsComplete"/> is false.
    /// </param>
    /// <param name="IsComplete">
    /// True when the record was whole (unchunked) or all of its chunks were present and validated.
    /// </param>
    /// <param name="MissingIndices">
    /// For an incomplete chunked record, the chunk indices that were not seen; empty otherwise.
    /// </param>
    public sealed record ReassembledRecord(string EventId, string? Json, bool IsComplete, IReadOnlyList<int> MissingIndices);
}

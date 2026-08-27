// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER

using Microsoft.Extensions.AI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace AWS.Bedrock.MEAI;

/// <summary>
/// Provider-neutral helpers shared by realtime and streaming audio sessions: concurrency guards,
/// role mapping, and tool-payload normalization. These invariants were first proven in
/// <see cref="BedrockNovaRealtimeSession"/>; they live here so other audio pipelines (for example
/// the AWS.Speech.MEAI voice loop) reuse the same tested behavior instead of reimplementing it.
/// </summary>
/// <remarks>
/// Everything here is pure or operates on a caller-owned field passed by reference, so it carries no
/// Nova Sonic protocol coupling. Nova-specific concerns (the outbound channel typing, ordered protocol
/// teardown, usage-event parsing, and the toolResult JSON-object wrapping) intentionally stay in
/// <see cref="BedrockNovaRealtimeSession"/>.
/// </remarks>
internal static class RealtimeAudioProtocol
{
    /// <summary>Maximum nesting depth for tool payloads to prevent stack overflow from malicious/malformed data.</summary>
    private const int MaxToolPayloadDepth = 64;

    /// <summary>
    /// Attempts to claim exclusive use of a single-reader stream by flipping <paramref name="flag"/> from 0 to 1.
    /// A single bidirectional stream can't safely serve two concurrent enumerations.
    /// </summary>
    /// <param name="flag">The caller-owned guard field (0 = free, 1 = in use).</param>
    /// <returns><see langword="true"/> if the caller now owns the enumeration; <see langword="false"/> if one is already active.</returns>
    public static bool TryBeginExclusiveEnumeration(ref int flag) =>
        Interlocked.CompareExchange(ref flag, 1, 0) == 0;

    /// <summary>Releases the guard claimed by <see cref="TryBeginExclusiveEnumeration"/>.</summary>
    /// <param name="flag">The caller-owned guard field.</param>
    public static void EndExclusiveEnumeration(ref int flag) =>
        Volatile.Write(ref flag, 0);

    /// <summary>Maps a role string (case-insensitive) to a <see cref="ChatRole"/>.</summary>
    public static ChatRole? MapRole(string? role) =>
        role?.ToUpperInvariant() switch
        {
            "USER" => ChatRole.User,
            "ASSISTANT" => ChatRole.Assistant,
            "SYSTEM" => ChatRole.System,
            "TOOL" => ChatRole.Tool,
            _ => null
        };

    /// <summary>
    /// Writes a normalized value (produced by <see cref="NormalizeToolPayload"/>) to a <see cref="Utf8JsonWriter"/>.
    /// Handles null, string, bool, numeric primitives, Dictionary, and List without reflection.
    /// </summary>
    public static void WriteNormalizedValue(object? value, Utf8JsonWriter writer)
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
            case float f:
                writer.WriteNumberValue(f);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case Dictionary<string, object?> dict:
                writer.WriteStartObject();
                foreach (var kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteNormalizedValue(kvp.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case List<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteNormalizedValue(item, writer);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    /// <summary>
    /// Recursively normalizes a tool payload into a tree of primitives, dictionaries, and lists.
    /// Handles JsonElement, byte[], nested dicts/lists, and enforces a maximum nesting depth.
    /// </summary>
    public static object? NormalizeToolPayload(object? value, int depth = 0)
    {
        ValidateToolPayloadDepth(depth);

        switch (value)
        {
            case null:
                return null;
            case byte[] bytes:
                return Convert.ToBase64String(bytes);
            case JsonElement element:
                return ConvertJsonElementToToolPayload(element, depth + 1);
            case JsonDocument document:
                return ConvertJsonElementToToolPayload(document.RootElement, depth + 1);
            case string:
            case bool:
            case int:
            case long:
            case float:
            case double:
            case decimal:
                return value;
            case IReadOnlyDictionary<string, object?> roDict:
                return NormalizeToolArguments(roDict, depth + 1);
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                return NormalizeToolArguments(
                    new Dictionary<string, object?>(pairs.Select(p => p), StringComparer.Ordinal), depth + 1);
            case IDictionary dict:
                var mapped = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dict)
                {
                    string key = entry.Key.ToString()!;
                    mapped[key] = NormalizeToolPayload(entry.Value, depth + 1);
                }
                return mapped;
            case IEnumerable<AIContent> aiContents:
                return aiContents.Select(content => NormalizeToolPayload(content, depth + 1)).ToList();
            case IEnumerable<object?> enumerable:
                var list = new List<object?>();
                foreach (var item in enumerable)
                {
                    list.Add(NormalizeToolPayload(item, depth + 1));
                }
                return list;
            default:
                return value.ToString();
        }
    }

    /// <summary>
    /// Normalizes a dictionary of tool arguments, recursively normalizing each value.
    /// </summary>
    public static Dictionary<string, object?> NormalizeToolArguments(IReadOnlyDictionary<string, object?> arguments, int depth = 0)
    {
        ValidateToolPayloadDepth(depth);

        var normalized = new Dictionary<string, object?>(arguments.Count, StringComparer.Ordinal);
        foreach (var pair in arguments)
        {
            normalized[pair.Key] = NormalizeToolPayload(pair.Value, depth + 1);
        }
        return normalized;
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to a tree of primitives, dictionaries, and lists.
    /// </summary>
    public static object? ConvertJsonElementToToolPayload(JsonElement element, int depth)
    {
        ValidateToolPayloadDepth(depth);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    dictionary[property.Name] = ConvertJsonElementToToolPayload(property.Value, depth + 1);
                }
                return dictionary;
            case JsonValueKind.Array:
                var arrayList = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    arrayList.Add(ConvertJsonElementToToolPayload(item, depth + 1));
                }
                return arrayList;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.TryGetInt64(out long l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static void ValidateToolPayloadDepth(int depth)
    {
        if (depth > MaxToolPayloadDepth)
        {
            throw new InvalidOperationException(
                $"Realtime tool payloads exceed the maximum supported nesting depth of {MaxToolPayloadDepth}.");
        }
    }
}

#endif

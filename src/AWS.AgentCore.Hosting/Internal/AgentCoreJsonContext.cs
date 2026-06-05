// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace AWS.AgentCore.Hosting.Internal;

/// <summary>
/// Source-generated JSON serialization context for AgentCore response types.
/// Enables NativeAOT-compatible JSON serialization without runtime reflection.
/// </summary>
[JsonSerializable(typeof(SseChunkResponse))]
[JsonSerializable(typeof(SseDoneResponse))]
[JsonSerializable(typeof(SseErrorResponse))]
[JsonSerializable(typeof(JsonMessageResponse))]
[JsonSerializable(typeof(JsonEmptyMessageResponse))]
[JsonSerializable(typeof(JsonErrorResponse))]
[JsonSerializable(typeof(PingResponse))]
internal partial class AgentCoreJsonContext : JsonSerializerContext;

internal record SseChunkResponse(string chunk);
internal record SseDoneResponse(string message, DateTime timestamp, bool done);
internal record SseErrorResponse(string error);
internal record JsonMessageResponse(string message, DateTime timestamp);
internal record JsonEmptyMessageResponse(string message, DateTime timestamp);
internal record JsonErrorResponse(string error);
internal record PingResponse(string status, long time_of_last_update);

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using NativeAotAnnotations.Models;

namespace NativeAotAnnotations;

[JsonSerializable(typeof(PromptRequest))]
[JsonSerializable(typeof(AppInfoResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext;

internal record PingResponse(string Status, long TimeOfLastUpdate);

internal record AppInfoResponse
{
    public string AppName { get; init; } = "";
    public bool IsNativeAot { get; init; }
    public string Framework { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Os { get; init; } = "";
}

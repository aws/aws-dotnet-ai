// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace AWS.Bedrock.MEAI;

internal sealed class EmbeddingRequest
{
    [JsonPropertyName("inputText")]
    public string? InputText { get; set; }

    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }
}

internal sealed class EmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }

    [JsonPropertyName("inputTextTokenCount")]
    public int? InputTextTokenCount { get; set; }
}
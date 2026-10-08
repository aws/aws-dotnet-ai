// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using AWS.Bedrock.MEAI;
using Microsoft.Extensions.AI;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Amazon.BedrockRuntime;

/// <summary>Provides extensions for working with <see cref="IAmazonBedrockRuntime"/> instances.</summary>
public static class AmazonBedrockRuntimeExtensions
{
    /// <summary>The provider name to use in metadata.</summary>
    internal const string ProviderName = "aws.bedrock";

    /// <summary>
    /// Key under which the native Bedrock stop-reason string (e.g. <c>"guardrail_intervened"</c> or
    /// <c>"content_filtered"</c>) is placed in <see cref="ChatResponse.AdditionalProperties"/> (and on the
    /// corresponding streaming <see cref="ChatResponseUpdate.AdditionalProperties"/>). Multiple native stop
    /// reasons map to a single <see cref="ChatFinishReason"/> (both guardrail/content-filter cases map to
    /// <see cref="ChatFinishReason.ContentFilter"/>), so this preserves the exact reason the service returned.
    /// </summary>
    public const string StopReasonKey = "StopReason";

    /// <summary>
    /// Key under which the Bedrock guardrail trace is placed in <see cref="ChatResponse.AdditionalProperties"/>
    /// (and on the streaming metadata <see cref="ChatResponseUpdate.AdditionalProperties"/>) when present. The
    /// value is a <see cref="System.Text.Json.JsonElement"/> serialized from the SDK trace object
    /// (<c>ConverseTrace</c> for non-streaming, <c>ConverseStreamTrace</c> for streaming) using
    /// source-generated metadata, so it is safe to serialize when the containing <see cref="ChatResponse"/> is
    /// persisted, including under trimming and Native AOT. New properties added to the SDK trace types in a
    /// future SDK version are surfaced automatically when this library is rebuilt against that SDK.
    /// </summary>
    public const string TraceKey = "Trace";

#if NET8_0_OR_GREATER
    /// <summary>Gets an <see cref="IRealtimeClient"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IRealtimeClient"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in session options. If not specified,
    /// a model must be provided in the <see cref="RealtimeSessionOptions.Model"/> passed to
    /// <see cref="IRealtimeClient.CreateSessionAsync"/>.
    /// </param>
    /// <returns>An <see cref="IRealtimeClient"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    [Experimental("MEAI001")]
    public static IRealtimeClient AsIRealtimeClient(this IAmazonBedrockRuntime runtime, string? defaultModelId = null) =>
        runtime is not null ? new BedrockNovaRealtimeClient(runtime, defaultModelId) :
        throw new ArgumentNullException(nameof(runtime));
#endif

    /// <summary>Gets an <see cref="IChatClient"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IChatClient"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in a request. If not specified,
    /// a model must be provided in the <see cref="ChatOptions.ModelId"/> passed to <see cref="IChatClient.GetResponseAsync"/>
    /// or <see cref="IChatClient.GetStreamingResponseAsync"/>.
    /// </param>
    /// <returns>A <see cref="IChatClient"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    public static IChatClient AsIChatClient(this IAmazonBedrockRuntime runtime, string? defaultModelId = null) =>
        runtime is not null ? new BedrockChatClient(runtime, defaultModelId) :
        throw new ArgumentNullException(nameof(runtime));

    /// <summary>Gets an <see cref="IChatClient"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IChatClient"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in a request. If not specified,
    /// a model must be provided in the <see cref="ChatOptions.ModelId"/> passed to <see cref="IChatClient.GetResponseAsync"/>
    /// or <see cref="IChatClient.GetStreamingResponseAsync"/>.
    /// </param>
    /// <param name="structuredOutputMode">
    /// Controls how <see cref="ChatOptions.ResponseFormat"/> is realized.
    /// <see cref="BedrockStructuredOutputMode.Native"/> uses Bedrock native structured outputs
    /// (composes with user-provided tools and supports streaming) and requires a model that supports
    /// the feature. Use <see cref="BedrockStructuredOutputMode.SyntheticTool"/> for models without
    /// native support.
    /// </param>
    /// <returns>A <see cref="IChatClient"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    public static IChatClient AsIChatClient(this IAmazonBedrockRuntime runtime, string? defaultModelId,
        BedrockStructuredOutputMode structuredOutputMode) =>
        runtime is not null ? new BedrockChatClient(runtime, defaultModelId, structuredOutputMode) :
        throw new ArgumentNullException(nameof(runtime));

    /// <summary>Gets an <see cref="IChatClient"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IChatClient"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in a request. If not specified,
    /// a model must be provided in the <see cref="ChatOptions.ModelId"/> passed to <see cref="IChatClient.GetResponseAsync"/>
    /// or <see cref="IChatClient.GetStreamingResponseAsync"/>.
    /// </param>
    /// <param name="options">
    /// Optional settings controlling request/response mapping, such as
    /// <see cref="BedrockChatClientOptions.StructuredOutputMode"/> and
    /// <see cref="BedrockChatClientOptions.CoalesceConsecutiveMessages"/>. When <see langword="null"/>,
    /// defaults are used.
    /// </param>
    /// <returns>A <see cref="IChatClient"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    public static IChatClient AsIChatClient(this IAmazonBedrockRuntime runtime, string? defaultModelId,
        BedrockChatClientOptions? options) =>
        runtime is not null ? new BedrockChatClient(runtime, defaultModelId, options) :
        throw new ArgumentNullException(nameof(runtime));

    /// <summary>Gets an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in a request. If not specified,
    /// a model must be provided in the <see cref="EmbeddingGenerationOptions.ModelId"/> passed to <see cref="IEmbeddingGenerator{TInput, TEmbedding}.GenerateAsync"/>.
    /// </param>
    /// <param name="defaultModelDimensions">
    /// The default number of dimensions to request be generated. This will be overridden by a <see cref="EmbeddingGenerationOptions.Dimensions"/>
    /// if that is specified to a request. If neither is specified, the default for the model will be used.
    /// </param>
    /// <returns>An <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    public static IEmbeddingGenerator<string, Embedding<float>> AsIEmbeddingGenerator(
        this IAmazonBedrockRuntime runtime, string? defaultModelId = null, int? defaultModelDimensions = null) =>
        runtime is not null ? new BedrockEmbeddingGenerator(runtime, defaultModelId, defaultModelDimensions) :
        throw new ArgumentNullException(nameof(runtime));

    /// <summary>Gets an <see cref="IImageGenerator"/> for the specified <see cref="IAmazonBedrockRuntime"/> instance.</summary>
    /// <param name="runtime">The runtime instance to be represented as an <see cref="IImageGenerator"/>.</param>
    /// <param name="defaultModelId">
    /// The default model ID to use when no model is specified in a request. If not specified,
    /// a model must be provided in the <see cref="ImageGenerationOptions.ModelId"/> passed to <see cref="IImageGenerator.GenerateAsync"/>.
    /// </param>
    /// <returns>An <see cref="IImageGenerator"/> instance representing the <see cref="IAmazonBedrockRuntime"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is <see langword="null"/>.</exception>
    [Experimental("MEAI001")]
    public static IImageGenerator AsIImageGenerator(
        this IAmazonBedrockRuntime runtime, string? defaultModelId = null) =>
        runtime is not null ? new BedrockImageGenerator(runtime, defaultModelId) :
        throw new ArgumentNullException(nameof(runtime));
}

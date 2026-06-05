// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Hosting;

/// <summary>
/// Marks a method as the AgentCore agent handler for <c>POST /invocations</c>.
/// The containing class is resolved from DI, so constructor-injected dependencies are available.
/// <para>
/// The return type determines the response format:
/// <c>Task&lt;string&gt;</c> for JSON, <c>IAsyncEnumerable&lt;string&gt;</c> for SSE streaming.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AgentCoreHandlerAttribute : Attribute
{
    /// <summary>
    /// The <see cref="System.Text.Json.Serialization.JsonSerializerContext"/> type that provides
    /// source-generated JSON metadata for the request type. Required for NativeAOT compatibility.
    /// The context must include <c>[JsonSerializable(typeof(TRequest))]</c> for the handler's request type.
    /// </summary>
    /// <example>
    /// <code>
    /// [AgentCoreHandler(JsonContext = typeof(AppJsonContext))]
    /// public async Task&lt;string&gt; Handle(PromptRequest request) => "ok";
    ///
    /// [JsonSerializable(typeof(PromptRequest))]
    /// internal partial class AppJsonContext : JsonSerializerContext;
    /// </code>
    /// </example>
    public Type? JsonContext { get; set; }
}

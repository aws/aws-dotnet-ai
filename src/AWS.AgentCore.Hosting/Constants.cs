// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Hosting;

/// <summary>
/// Constants used throughout the AWS.AgentCore library.
/// </summary>
internal static class Constants
{
    /// <summary>
    /// Environment variable name for the AgentCore Memory ID.
    /// When set, the Memory provider uses this value as the MemoryId for all Memory operations
    /// (unless overridden by <see cref="AgentCoreOptions.MemoryId"/>).
    /// </summary>
    internal const string MemoryIdEnvironmentVariable = "AWS_AGENTCORE_MEMORY_ID";

    /// <summary>
    /// Environment variable name for overriding the Amazon Bedrock AgentCore service endpoint.
    /// When set, the <see cref="Amazon.BedrockAgentCore.IAmazonBedrockAgentCore"/> client registered
    /// by <see cref="Extensions.AgentCoreBuilderExtensions.AddAgentCore"/> will use this URL as its
    /// ServiceURL instead of the default AWS endpoint. This enables local development with emulators.
    /// </summary>
    internal const string ServiceEndpointEnvironmentVariable = "AWS_AGENTCORE_SERVICE_ENDPOINT";

    /// <summary>
    /// Environment variable set by the Aspire Testing package to indicate that the agent
    /// is being managed by an Aspire AppHost. When set to <c>"true"</c>,
    /// <see cref="Extensions.AgentCoreBuilderExtensions.AddAgentCore"/> skips its default
    /// <c>UseUrls("http://0.0.0.0:8080")</c> binding, allowing Aspire DCP to allocate the port.
    /// </summary>
    internal const string AspireManagedEnvironmentVariable = "AWS_AGENTCORE_ASPIRE_MANAGED";
}

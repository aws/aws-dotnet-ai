// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Hosting.IntegrationTests.Infrastructure;

/// <summary>
/// Configuration for integration tests, loaded from environment variables.
/// </summary>
public static class TestConfiguration
{
    /// <summary>AWS region for all resources (default: us-west-2).</summary>
    public static string Region => Environment.GetEnvironmentVariable("AGENTCORE_TEST_REGION") ?? "us-west-2";

    /// <summary>Unique prefix for test resources to avoid collisions (max 15 chars to fit runtime name limit).</summary>
    public static string TestRunId { get; } = $"{DateTime.UtcNow:MMddHHmmss}{Guid.NewGuid().ToString("N")[..4]}";

    /// <summary>Shared resource manager — creates IAM role, ECR repo, and cleans up on dispose.</summary>
    public static TestResourceManager Resources { get; } = new(Region, TestRunId);
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Xunit;

[assembly: AssemblyFixture(typeof(AWS.AgentCore.IntegrationTests.Infrastructure.GlobalCleanup))]

namespace AWS.AgentCore.IntegrationTests.Infrastructure;

/// <summary>
/// Assembly-level fixture that ensures all shared AWS resources (IAM role, ECR repo)
/// are cleaned up after all tests complete, regardless of pass/fail status.
/// </summary>
public class GlobalCleanup : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await TestConfiguration.Resources.DisposeAsync();

        // Clean up temp Docker config directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"docker-ecr-{TestConfiguration.TestRunId}");
        if (Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Hosting.IntegrationTests.Infrastructure;

/// <summary>
/// Shared fixture that provides the runtime ARN for a sample app.
/// All infrastructure (IAM, ECR, Docker build, push, runtime creation) is handled
/// by <see cref="TestResourceManager"/> using CloudFormation stacks.
/// </summary>
public class SampleAppFixture : IAsyncLifetime
{
    private readonly string _sampleAppName;

    public string RuntimeArn { get; private set; } = "";
    public string Region => TestConfiguration.Region;

    public SampleAppFixture(string sampleAppName)
    {
        _sampleAppName = sampleAppName;
    }

    public async ValueTask InitializeAsync()
    {
        // TestResourceManager handles everything: base stack, Docker build/push, runtimes stack.
        // First fixture to call this triggers the full initialization; others wait on the semaphore.
        RuntimeArn = await TestConfiguration.Resources.GetRuntimeArnAsync(_sampleAppName);
        Console.Error.WriteLine($"[SampleApp] {_sampleAppName} runtime ARN: {RuntimeArn}");
    }

    public ValueTask DisposeAsync()
    {
        // Cleanup is handled by TestResourceManager (deletes both CFN stacks).
        return ValueTask.CompletedTask;
    }
}

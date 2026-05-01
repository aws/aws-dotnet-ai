// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Amazon;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using InvalidOperationException = System.InvalidOperationException;

namespace AWS.AgentCore.IntegrationTests.Infrastructure;

/// <summary>
/// Manages all AWS resources for integration tests using two CloudFormation stacks:
/// <list type="number">
///   <item><b>Base stack</b> — IAM role and ECR repository (shared resources).</item>
///   <item><b>Runtimes stack</b> — All AgentCore Runtime resources (one per sample app).</item>
/// </list>
/// Between the two stacks, Docker images are built and pushed to ECR.
/// Cleanup deletes both stacks — CloudFormation handles teardown ordering.
/// </summary>
public sealed class TestResourceManager : IAsyncDisposable
{
    private readonly string _region;
    private readonly string _testRunId;

    private readonly AmazonCloudFormationClient _cfnClient;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;
    private volatile Exception? _initError;

    private string? _baseStackName;
    private string? _runtimesStackName;
    private string? _roleArn;
    private string? _ecrRepositoryUri;
    private string? _ecrRepositoryName;

    /// <summary>Runtime ARNs keyed by sample app name (e.g. "MicrosoftAgentFrameworkSample").</summary>
    private readonly Dictionary<string, string> _runtimeArns = new();

    /// <summary>
    /// All sample apps that need runtimes. Register before initialization.
    /// </summary>
    private static readonly string[] SampleApps =
    [
        "MicrosoftAgentFrameworkSample",
        "AnnotationsSample",
        "StreamingAgent",
        "AnnotationsStreamingAgent",
        "NativeAotExtensions",
        "NativeAotAnnotations",
    ];

    public TestResourceManager(string region, string testRunId)
    {
        _region = region;
        _testRunId = testRunId;
        _cfnClient = new AmazonCloudFormationClient(RegionEndpoint.GetBySystemName(region));
    }

    /// <summary>Gets the runtime ARN for a specific sample app. Blocks until all resources are ready.</summary>
    public async Task<string> GetRuntimeArnAsync(string sampleAppName, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!_runtimeArns.TryGetValue(sampleAppName, out var arn))
            throw new InvalidOperationException(
                $"No runtime ARN found for '{sampleAppName}'. Registered apps: {string.Join(", ", _runtimeArns.Keys)}");
        return arn;
    }

    /// <summary>Gets the IAM role ARN for AgentCore runtimes.</summary>
    public async Task<string> GetRoleArnAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _roleArn!;
    }

    /// <summary>Gets the ECR repository URI.</summary>
    public async Task<string> GetEcrRepositoryUriAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        return _ecrRepositoryUri!;
    }

    public string Region => _region;

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        // Fast path: previous initialization failed
        if (_initError is not null)
            throw new InvalidOperationException("Test resource initialization previously failed.", _initError);

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            if (_initError is not null)
                throw new InvalidOperationException("Test resource initialization previously failed.", _initError);

            // Phase 1: Create base stack (IAM role + ECR repo)
            await CreateBaseStackAsync(ct);

            // Phase 2: Build and push all Docker images
            var imageUris = await BuildAndPushAllImagesAsync(ct);

            // Phase 3: Create runtimes stack (all AgentCore runtimes)
            await CreateRuntimesStackAsync(imageUris, ct);

            _initialized = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _initError = ex;
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Phase 1: Base stack
    // ──────────────────────────────────────────────────────────────────

    private async Task CreateBaseStackAsync(CancellationToken ct)
    {
        _baseStackName = $"aws-dotnet-ai-integ-tests-base-{_testRunId}".ToLowerInvariant();
        var templateBody = ReadEmbeddedTemplate("inttest-stack.template.json");

        Console.Error.WriteLine($"[Resources] Creating base stack: {_baseStackName}");

        await _cfnClient.CreateStackAsync(new CreateStackRequest
        {
            StackName = _baseStackName,
            TemplateBody = templateBody,
            Parameters = new List<Parameter>
            {
                new() { ParameterKey = "TestRunId", ParameterValue = _testRunId },
            },
            Capabilities = new List<string> { "CAPABILITY_NAMED_IAM" },
            Tags = CreateTags(),
            OnFailure = OnFailure.DELETE,
        }, ct);

        await WaitForStackAsync(_baseStackName, StackStatus.CREATE_COMPLETE, TimeSpan.FromMinutes(5), ct);

        // Read outputs
        var outputs = await GetStackOutputsAsync(_baseStackName, ct);

        _roleArn = outputs["RoleArn"];
        _ecrRepositoryUri = outputs["EcrRepositoryUri"];
        _ecrRepositoryName = outputs["EcrRepositoryName"];

        Console.Error.WriteLine($"[Resources] Base stack ready. Role={_roleArn}, ECR={_ecrRepositoryUri}");
    }

    // ──────────────────────────────────────────────────────────────────
    // Phase 2: Build and push images
    // ──────────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, string>> BuildAndPushAllImagesAsync(CancellationToken ct)
    {
        var imageUris = new Dictionary<string, string>();

        foreach (var appName in SampleApps)
        {
            var imageTag = $"{appName.ToLowerInvariant()}-{_testRunId}";
            var localTag = $"agentcore-test/{imageTag}";

            // Build (serialized via DockerHelper's internal semaphore)
            await DockerHelper.BuildImageAsync(appName, localTag, ct);

            // Push to ECR
            var ecrImageUri = await DockerHelper.PushToEcrAsync(localTag, _ecrRepositoryUri!, imageTag, _region, ct);
            imageUris[appName] = ecrImageUri;

            Console.Error.WriteLine($"[Resources] Pushed {appName} → {ecrImageUri}");
        }

        return imageUris;
    }

    // ──────────────────────────────────────────────────────────────────
    // Phase 3: Runtimes stack
    // ──────────────────────────────────────────────────────────────────

    private async Task CreateRuntimesStackAsync(Dictionary<string, string> imageUris, CancellationToken ct)
    {
        _runtimesStackName = $"aws-dotnet-ai-integ-tests-runtimes-{_testRunId}".ToLowerInvariant();
        var templateBody = ReadEmbeddedTemplate("inttest-runtimes.template.json");

        Console.Error.WriteLine($"[Resources] Creating runtimes stack: {_runtimesStackName} ({imageUris.Count} runtimes)");

        var parameters = new List<Parameter>
        {
            new() { ParameterKey = "TestRunId", ParameterValue = _testRunId },
            new() { ParameterKey = "RoleArn", ParameterValue = _roleArn! },
        };

        foreach (var (appName, imageUri) in imageUris)
        {
            parameters.Add(new Parameter
            {
                ParameterKey = $"{appName}ImageUri",
                ParameterValue = imageUri,
            });
        }

        await _cfnClient.CreateStackAsync(new CreateStackRequest
        {
            StackName = _runtimesStackName,
            TemplateBody = templateBody,
            Parameters = parameters,
            Tags = CreateTags(),
            OnFailure = OnFailure.DELETE,
        }, ct);

        // AgentCore runtimes can take a while to reach READY
        await WaitForStackAsync(_runtimesStackName, StackStatus.CREATE_COMPLETE, TimeSpan.FromMinutes(10), ct);

        // Read runtime ARNs from outputs
        var outputs = await GetStackOutputsAsync(_runtimesStackName, ct);
        foreach (var appName in SampleApps)
        {
            var outputKey = appName + "Arn";
            if (outputs.TryGetValue(outputKey, out var arn))
            {
                _runtimeArns[appName] = arn;
                Console.Error.WriteLine($"[Resources] Runtime {appName} → {arn}");
            }
            else
            {
                Console.Error.WriteLine($"[Resources] WARNING: No output '{outputKey}' found for {appName}");
            }
        }

        Console.Error.WriteLine($"[Resources] Runtimes stack ready. {_runtimeArns.Count} runtimes created.");
    }

    // ──────────────────────────────────────────────────────────────────
    // Stack helpers
    // ──────────────────────────────────────────────────────────────────

    private async Task WaitForStackAsync(string stackName, StackStatus targetStatus, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var pollCount = 0;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            DescribeStacksResponse response;
            try
            {
                response = await _cfnClient.DescribeStacksAsync(new DescribeStacksRequest
                {
                    StackName = stackName,
                }, ct);
            }
            catch (AmazonCloudFormationException ex) when (ex.Message.Contains("does not exist"))
            {
                if (targetStatus == StackStatus.DELETE_COMPLETE)
                    return; // Stack gone = delete complete
                throw new InvalidOperationException($"Stack {stackName} does not exist.");
            }

            var stack = response.Stacks.FirstOrDefault()
                ?? throw new InvalidOperationException($"Stack {stackName} not found.");

            var status = stack.StackStatus;
            pollCount++;
            Console.Error.WriteLine($"[CFN] Stack {stackName} status: {status.Value} (poll #{pollCount})");

            if (status == targetStatus)
                return;

            if (status == StackStatus.CREATE_FAILED ||
                status == StackStatus.ROLLBACK_COMPLETE ||
                status == StackStatus.ROLLBACK_FAILED ||
                status == StackStatus.ROLLBACK_IN_PROGRESS)
            {
                // Dump stack events for debugging
                await DumpStackEventsAsync(stackName, ct);
                var reason = stack.StackStatusReason ?? "Unknown";
                throw new InvalidOperationException(
                    $"Stack {stackName} reached {status.Value}. Reason: {reason}");
            }

            // If we're waiting for CREATE_COMPLETE but the stack is being deleted
            // (OnFailure=DELETE kicked in), treat it as a creation failure.
            if (targetStatus == StackStatus.CREATE_COMPLETE &&
                (status == StackStatus.DELETE_IN_PROGRESS || status == StackStatus.DELETE_COMPLETE))
            {
                await DumpStackEventsAsync(stackName, ct);
                throw new InvalidOperationException(
                    $"Stack {stackName} creation failed and is being deleted (status: {status.Value}).");
            }

            if (targetStatus == StackStatus.DELETE_COMPLETE &&
                status == StackStatus.DELETE_FAILED)
            {
                throw new InvalidOperationException(
                    $"Stack {stackName} deletion failed. Reason: {stack.StackStatusReason ?? "Unknown"}");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }

        throw new TimeoutException(
            $"Stack {stackName} did not reach {targetStatus.Value} within {timeout.TotalMinutes} minutes.");
    }

    private async Task<Dictionary<string, string>> GetStackOutputsAsync(string stackName, CancellationToken ct)
    {
        var response = await _cfnClient.DescribeStacksAsync(new DescribeStacksRequest
        {
            StackName = stackName,
        }, ct);

        return response.Stacks.First().Outputs
            .ToDictionary(o => o.OutputKey, o => o.OutputValue);
    }

    private async Task DumpStackEventsAsync(string stackName, CancellationToken ct)
    {
        try
        {
            var response = await _cfnClient.DescribeStackEventsAsync(new DescribeStackEventsRequest
            {
                StackName = stackName,
            }, ct);

            Console.Error.WriteLine($"[CFN] Stack events for {stackName}:");
            foreach (var evt in response.StackEvents.Take(20))
            {
                Console.Error.WriteLine(
                    $"[CFN]   {evt.Timestamp:u} {evt.LogicalResourceId} {evt.ResourceStatus} {evt.ResourceStatusReason}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CFN] Failed to dump stack events: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Cleanup
    // ──────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Dump CloudWatch logs before deleting runtimes
        foreach (var (appName, arn) in _runtimeArns)
        {
            try
            {
                Console.Error.WriteLine($"[Cleanup] Dumping CloudWatch logs for {appName}...");
                await CloudWatchLogHelper.DumpRuntimeLogsAsync(arn, _region);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Cleanup] Failed to dump logs for {appName}: {ex.Message}");
            }
        }

        // Delete runtimes stack first (depends on base stack resources)
        await DeleteStackAsync(_runtimesStackName);

        // Force-delete ECR repo (CFN can't delete non-empty repos)
        if (!string.IsNullOrEmpty(_ecrRepositoryName))
        {
            try
            {
                using var ecrClient = new Amazon.ECR.AmazonECRClient(RegionEndpoint.GetBySystemName(_region));
                await ecrClient.DeleteRepositoryAsync(new Amazon.ECR.Model.DeleteRepositoryRequest
                {
                    RepositoryName = _ecrRepositoryName,
                    Force = true,
                });
                Console.Error.WriteLine($"[Cleanup] Force-deleted ECR repository: {_ecrRepositoryName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Cleanup] Failed to force-delete ECR repo: {ex.Message}");
            }
        }

        // Delete base stack
        await DeleteStackAsync(_baseStackName);

        _cfnClient.Dispose();
        _initLock.Dispose();
    }

    private async Task DeleteStackAsync(string? stackName)
    {
        if (string.IsNullOrEmpty(stackName)) return;

        Console.Error.WriteLine($"[Cleanup] Deleting stack: {stackName}");
        try
        {
            await _cfnClient.DeleteStackAsync(new DeleteStackRequest { StackName = stackName });
            await WaitForStackAsync(stackName, StackStatus.DELETE_COMPLETE, TimeSpan.FromMinutes(10), CancellationToken.None);
            Console.Error.WriteLine($"[Cleanup] Stack {stackName} deleted.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Cleanup] Failed to delete stack {stackName}: {ex.Message}");
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Utilities
    // ──────────────────────────────────────────────────────────────────

    private List<Amazon.CloudFormation.Model.Tag> CreateTags() =>
    [
        new() { Key = "CreatedBy", Value = "AgentCoreIntegrationTests" },
        new() { Key = "TestRunId", Value = _testRunId },
        new() { Key = "aws-repo", Value = "aws-dotnet-ai" },
    ];

    private static string ReadEmbeddedTemplate(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(fileName));

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

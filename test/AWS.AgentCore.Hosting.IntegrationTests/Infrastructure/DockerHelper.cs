// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AWS.AgentCore.Hosting.IntegrationTests.Infrastructure;

/// <summary>
/// Builds container images for sample apps and pushes them to ECR.
/// Uses <c>dotnet publish /t:PublishContainer</c> for IL-based apps (no Dockerfile needed,
/// cross-platform via -r linux-arm64) and Docker buildx for NativeAOT apps.
/// </summary>
public static class DockerHelper
{
    private static readonly SemaphoreSlim _loginLock = new(1, 1);
    private static readonly SemaphoreSlim _buildLock = new(1, 1);
    private static volatile string? _dockerConfigDir;
    private static volatile Exception? _loginError;

    /// <summary>
    /// Builds a container image for the given sample app using <c>dotnet publish /t:PublishContainer</c>.
    /// Produces ARM64 Linux containers (required by AgentCore).
    /// Works on ARM64 Linux (CI), ARM64 macOS, and AMD64 Windows dev machines.
    /// </summary>
    public static async Task BuildImageAsync(string sampleAppName, string imageTag, CancellationToken ct = default)
    {
        // Serialize all dotnet publish calls to avoid file locking conflicts.
        // Multiple fixtures initialize concurrently, and parallel builds fight over
        // shared output files (e.g. AWS.AgentCore.SourceGenerator.deps.json).
        await _buildLock.WaitAsync(ct);
        try
        {
            var repoRoot = FindRepoRoot();
            var projectPath = Path.Combine("sampleapps", sampleAppName, $"{sampleAppName}.csproj");

            Console.WriteLine($"[Docker] Building {sampleAppName} via dotnet publish /t:PublishContainer");

            await RunProcessAsync("dotnet", $"publish {projectPath} -r linux-arm64 -c Release " +
                $"/t:PublishContainer " +
                $"/p:ContainerImageName={imageTag} " +
                $"/p:ContainerImageTag=latest " +
                $"/p:EnableSdkContainerSupport=true",
                workingDirectory: repoRoot, ct: ct);

            // Verify the image exists in the local Docker daemon
            Console.Error.WriteLine($"[Docker] Verifying image {imageTag}:latest exists locally...");
            try
            {
                var inspectOutput = await RunProcessAsync("docker", $"image inspect {imageTag}:latest --format '{{{{.Id}}}}'", ct: ct);
                Console.Error.WriteLine($"[Docker] Image found: {inspectOutput.Trim()}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Docker] WARNING: Image {imageTag}:latest not found in local Docker daemon: {ex.Message}");
                // List all images to help debug
                try
                {
                    var listOutput = await RunProcessAsync("docker", "images --format '{{.Repository}}:{{.Tag}}'", ct: ct);
                    Console.Error.WriteLine($"[Docker] Available images:\n{listOutput}");
                }
                catch { /* best effort */ }
            }
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <summary>
    /// Authenticates Docker to ECR, tags the image, and pushes it.
    /// Returns the full ECR image URI.
    /// Uses a temporary Docker config directory to bypass macOS keychain issues.
    /// </summary>
    public static async Task<string> PushToEcrAsync(
        string localImageTag,
        string ecrRepositoryUri,
        string remoteTag,
        string region,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ecrRepositoryUri, nameof(ecrRepositoryUri));

        var registryUri = ecrRepositoryUri.Split('/')[0];

        // Ensure ECR login is done once, using a temp config to avoid keychain issues
        await EnsureEcrLoginAsync(registryUri, region, ct);

        var configDir = _dockerConfigDir
            ?? throw new InvalidOperationException("Docker ECR login was not initialized. _dockerConfigDir is null after EnsureEcrLoginAsync.");

        var configFlag = $"--config {configDir}";

        // Tag and push — the image from PublishContainer is tagged as localImageTag:latest
        var fullUri = $"{ecrRepositoryUri}:{remoteTag}";
        Console.Error.WriteLine($"[Docker] Tagging {localImageTag}:latest as {fullUri}");
        await RunProcessAsync("docker", $"tag {localImageTag}:latest {fullUri}", ct: ct);

        // Push with retry — ECR pushes can fail with transient TCP resets
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Console.Error.WriteLine($"[Docker] Pushing {fullUri} (attempt {attempt})");
                await RunProcessAsync("docker", $"{configFlag} push {fullUri}", ct: ct);
                Console.Error.WriteLine($"[Docker] Push complete: {fullUri}");
                break;
            }
            catch (Exception ex) when (attempt < 3)
            {
                Console.Error.WriteLine($"[Docker] Push attempt {attempt} failed: {ex.Message}. Retrying in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }

        return fullUri;
    }

    /// <summary>
    /// Creates a temporary Docker config with ECR credentials, bypassing the OS credential store.
    /// Thread-safe — only runs once even with concurrent fixture initialization.
    /// Caches any error so subsequent callers get the same failure immediately.
    /// </summary>
    private static async Task EnsureEcrLoginAsync(string registryUri, string region, CancellationToken ct)
    {
        // Fast path: already initialized
        if (_dockerConfigDir is not null) return;

        // Fast path: previous initialization failed
        if (_loginError is not null)
            throw new InvalidOperationException("Docker ECR login previously failed.", _loginError);

        await _loginLock.WaitAsync(ct);
        try
        {
            if (_dockerConfigDir is not null) return;
            if (_loginError is not null)
                throw new InvalidOperationException("Docker ECR login previously failed.", _loginError);

            // Get ECR auth token
            var loginPassword = await RunProcessAsync("aws", $"ecr get-login-password --region {region}", ct: ct);
            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"AWS:{loginPassword.Trim()}"));

            // Write a temporary Docker config.json with the auth token directly
            // This bypasses the macOS keychain credential helper entirely
            var tempDir = Path.Combine(Path.GetTempPath(), $"docker-ecr-{TestConfiguration.TestRunId}");
            Directory.CreateDirectory(tempDir);

            var config = new
            {
                auths = new Dictionary<string, object>
                {
                    [$"https://{registryUri}"] = new { auth = authToken }
                }
            };

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempDir, "config.json"), configJson, ct);

            _dockerConfigDir = tempDir;
            Console.WriteLine($"[Docker] ECR login configured via temp config at {tempDir}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loginError = ex;
            throw;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AWS.DotNetAI.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (looking for AWS.DotNetAI.slnx).");
    }

    private static async Task<string> RunProcessAsync(
        string fileName, string arguments, string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        var stdout = stdoutTask.Result;
        var stderr = stderrTask.Result;

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName} {arguments}' exited with code {process.ExitCode}.\nStdout: {stdout}\nStderr: {stderr}");
        }

        return stdout;
    }
}

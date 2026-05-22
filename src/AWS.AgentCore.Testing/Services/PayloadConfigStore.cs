// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// Persists payload editor configuration (template + parameters) to disk,
/// keyed by agent name. Stored in ~/.agentcore/testing/{agentName}/payload-config.json.
/// This allows each agent project to retain its custom request shape across restarts.
/// </summary>
internal sealed class PayloadConfigStore
{
    private readonly string _configPath;

    public string AgentName { get; }

    public PayloadConfigStore(string agentName)
    {
        AgentName = agentName;
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agentcore", "testing", agentName);
        Directory.CreateDirectory(configDir);
        _configPath = Path.Combine(configDir, "payload-config.json");
    }

    /// <summary>
    /// Loads the saved payload configuration, or null if none exists.
    /// </summary>
    public string? Load()
    {
        return File.Exists(_configPath) ? File.ReadAllText(_configPath) : null;
    }

    /// <summary>
    /// Saves the payload configuration to disk.
    /// </summary>
    public void Save(string json)
    {
        File.WriteAllText(_configPath, json);
    }

    /// <summary>
    /// Deletes the saved configuration, resetting to defaults.
    /// </summary>
    public void Delete()
    {
        if (File.Exists(_configPath))
            File.Delete(_configPath);
    }
}

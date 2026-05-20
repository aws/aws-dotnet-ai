// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace RemoteMcpAgent;

/// <summary>
/// Manages MCP client connections and provides tools to the agent.
/// Connects lazily on first use and caches the connection for subsequent requests.
/// </summary>
public class McpToolProvider : IAsyncDisposable
{
    private readonly Dictionary<string, Uri> _servers = new()
    {
        ["DeepWiki"] = new("https://mcp.deepwiki.com/mcp")
    };

    private readonly List<McpClient> _clients = [];
    private readonly List<AITool> _tools = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _connected;

    public IReadOnlyList<AITool> Tools => _tools;

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (_connected) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connected) return;

            foreach (var (name, url) in _servers)
            {
                try
                {
                    Console.WriteLine($"[MCP] Connecting to '{name}' at {url}...");

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    var transport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Endpoint = url
                    });

                    var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
                    _clients.Add(client);

                    var mcpTools = await client.ListToolsAsync(cancellationToken: cts.Token);
                    _tools.AddRange(mcpTools);

                    Console.WriteLine($"[MCP] Connected to '{name}' — {mcpTools.Count} tool(s): {string.Join(", ", mcpTools.Select(t => t.Name))}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MCP] WARNING: Failed to connect to '{name}': {ex.Message}");
                }
            }

            Console.WriteLine($"[MCP] Total tools registered: {_tools.Count}");
            _connected = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Sockets;

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// Utility for pre-allocating available TCP ports.
/// Uses the OS to find a free port by binding to port 0 and reading the assigned port.
/// </summary>
internal static class PortAllocator
{
    /// <summary>
    /// Gets an available TCP port by briefly binding to port 0 on loopback.
    /// The port is released immediately after discovery, so there's a small
    /// window where another process could claim it — acceptable for local dev.
    /// </summary>
    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

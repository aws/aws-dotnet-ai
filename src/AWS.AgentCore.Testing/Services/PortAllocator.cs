// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AWS.AgentCore.Testing.Services;

/// <summary>
/// Utility for pre-allocating available TCP ports.
/// Uses the OS to find free ports by binding to port 0 and reading the assigned port.
/// Tracks all allocated ports to avoid returning duplicates within the same process.
/// </summary>
internal static class PortAllocator
{
    private static readonly ConcurrentDictionary<int, byte> _allocatedPorts = new();

    /// <summary>
    /// Gets an available TCP port by briefly binding to port 0 on loopback.
    /// Retries if the OS returns a port already allocated in this process.
    /// </summary>
    public static int GetAvailablePort()
    {
        const int maxAttempts = 20;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            socket.Close();

            if (_allocatedPorts.TryAdd(port, 0))
                return port;
        }

        throw new InvalidOperationException(
            $"Failed to allocate a unique port after {maxAttempts} attempts.");
    }
}

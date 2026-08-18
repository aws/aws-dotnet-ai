// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AWS.Bedrock.MAG.UnitTests.Mcp
{
    /// <summary>An MCP tool that returns a fixed result, for decorator tests.</summary>
    internal sealed class StubTool : McpServerTool
    {
        private readonly CallToolResult _result;

        public StubTool(string name, CallToolResult result)
        {
            _result = result;
            ProtocolTool = new Tool { Name = name };
        }

        public override Tool ProtocolTool { get; }

        public override IReadOnlyList<object> Metadata => Array.Empty<object>();

        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_result);
    }

    internal static class McpTest
    {
        public static RequestContext<CallToolRequestParams> Request(string toolName = "stub")
        {
            var jsonRpc = new JsonRpcRequest { Id = new RequestId("1"), Method = RequestMethods.ToolsCall };
            return new RequestContext<CallToolRequestParams>(
                new TestMcpServer(NullServiceProvider.Instance),
                jsonRpc,
                new CallToolRequestParams { Name = toolName, Arguments = new Dictionary<string, JsonElement>() });
        }
    }

#pragma warning disable MCPEXP002
    internal sealed class TestMcpServer : McpServer
    {
        private readonly IServiceProvider _services;

        public TestMcpServer(IServiceProvider services) => _services = services;

        public override string? SessionId => "test-session";

        public override string? NegotiatedProtocolVersion => "2025-03-26";

        public override ClientCapabilities? ClientCapabilities => null;

        public override Implementation? ClientInfo => null;

        public override McpServerOptions ServerOptions { get; } = new();

        public override IServiceProvider? Services => _services;

        public override LoggingLevel? LoggingLevel => null;

        public override Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override IAsyncDisposable RegisterNotificationHandler(string method, Func<JsonRpcNotification, CancellationToken, ValueTask> handler)
            => new NoOpAsyncDisposable();

        public override Task RunAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public override ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
#pragma warning restore MCPEXP002

    internal sealed class NoOpAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    internal sealed class NullServiceProvider : IServiceProvider
    {
        public static readonly NullServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}

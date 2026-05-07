// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MicrosoftAgentFrameworkSample;

/// <summary>
/// Demonstrates Microsoft Agent Framework middleware patterns.
/// These middleware functions intercept agent runs and tool invocations.
/// </summary>
public static class AgentMiddleware
{
    /// <summary>
    /// Agent-level middleware: intercepts every agent run, logging input/output message counts.
    /// This runs before and after the entire agent pipeline (including tool calls).
    /// </summary>
    public static async Task<AgentResponse> LoggingMiddleware(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        var messageCount = messages.Count();
        Console.WriteLine($"[Middleware] Agent run starting — {messageCount} input message(s)");

        var response = await innerAgent.RunAsync(messages, session, options, cancellationToken);

        Console.WriteLine($"[Middleware] Agent run complete — {response.Messages.Count} response message(s)");

        return response;
    }

    /// <summary>
    /// Function-calling middleware: intercepts every tool invocation, logging tool name and result.
    /// This runs each time the LLM decides to call a tool.
    /// </summary>
    public static async ValueTask<object?> ToolExecutionMiddleware(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ToolMiddleware] Calling tool: {context.Function.Name}");

        var result = await next(context, cancellationToken);

        var resultStr = result?.ToString() ?? "(null)";
        var preview = resultStr.Length > 100 ? resultStr[..100] + "..." : resultStr;
        Console.WriteLine($"[ToolMiddleware] Tool {context.Function.Name} returned: {preview}");

        return result;
    }
}

namespace AWS.AgentCore.Testing.Emulators.Runtime.Models;

/// <summary>
/// Result returned after invoking the agent via the runtime emulator.
/// </summary>
/// <param name="SessionId">The session identifier used for this invocation.</param>
/// <param name="RequestId">The unique request identifier generated for this invocation.</param>
/// <param name="Response">The agent's response text.</param>
/// <param name="IsStreaming">Whether the response was received as an SSE stream.</param>
/// <param name="Timestamp">UTC timestamp when the response was received.</param>
public record InvocationResult(
    string SessionId,
    string RequestId,
    string Response,
    bool IsStreaming,
    DateTime Timestamp
);

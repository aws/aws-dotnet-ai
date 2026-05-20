namespace AWS.AgentCore.Testing.Emulators.Runtime.Models;

/// <summary>
/// Result for stream-through invocations where the raw SSE stream from the agent
/// is passed directly to the caller without buffering.
/// </summary>
/// <param name="SessionId">The session identifier used for this invocation.</param>
/// <param name="ResponseStream">The raw SSE response stream from the agent to pipe to the client.</param>
public record StreamThroughResult(
    string SessionId,
    Stream ResponseStream
);

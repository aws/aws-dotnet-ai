namespace AWS.AgentCore.Testing.Emulators.Runtime.Models;

/// <summary>
/// Tracks the state of an active conversation session in the runtime emulator.
/// </summary>
/// <param name="SessionId">The unique session identifier.</param>
/// <param name="CreatedAt">UTC timestamp when this session was first created.</param>
/// <param name="LastActivityAt">UTC timestamp of the most recent invocation in this session.</param>
/// <param name="InvocationCount">Total number of invocations made in this session.</param>
public record SessionState(
    string SessionId,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    int InvocationCount
);

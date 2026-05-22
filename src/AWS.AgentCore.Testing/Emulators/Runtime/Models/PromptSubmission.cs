namespace AWS.AgentCore.Testing.Emulators.Runtime.Models;

/// <summary>
/// A request to invoke the agent with a JSON payload.
/// </summary>
/// <param name="Text">The raw JSON payload to forward to the agent's /invocations endpoint.</param>
/// <param name="SessionId">Optional session identifier. When null, a new session ID is generated.</param>
public record PromptSubmission(
    string Text,
    string? SessionId = null
);

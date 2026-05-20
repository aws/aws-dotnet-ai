namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Content model matching the AWS SDK wire format for memory event payloads.
/// </summary>
public class ContentModel
{
    /// <summary>
    /// The text content of the message (user prompt or assistant response).
    /// </summary>
    public string? Text { get; set; }
}

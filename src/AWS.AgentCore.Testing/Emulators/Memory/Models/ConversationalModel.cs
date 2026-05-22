namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Conversational payload matching the AWS SDK wire format for memory events.
/// Represents a single turn in a conversation.
/// </summary>
public class ConversationalModel
{
    /// <summary>
    /// The role of the speaker. Valid values: "USER" or "ASSISTANT".
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// The content of this conversational turn.
    /// </summary>
    public ContentModel? Content { get; set; }
}

namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Payload type wrapper matching the AWS SDK wire format.
/// Currently supports conversational payloads (chat messages).
/// </summary>
public class PayloadTypeModel
{
    /// <summary>
    /// The conversational payload containing a role and text content.
    /// </summary>
    public ConversationalModel? Conversational { get; set; }
}

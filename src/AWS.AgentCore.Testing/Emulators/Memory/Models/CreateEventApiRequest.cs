namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Request body for the CreateEvent API endpoint.
/// Matches the JSON format sent by the AWS SDK's IAmazonBedrockAgentCore client.
/// </summary>
public class CreateEventApiRequest
{
    /// <summary>
    /// The actor (user or agent) that generated this event.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// The session this event belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Event timestamp as Unix epoch seconds. When null, the server uses the current time.
    /// </summary>
    public double? EventTimestamp { get; set; }

    /// <summary>
    /// The event payload containing conversational data.
    /// </summary>
    public List<PayloadTypeModel> Payload { get; set; } = [];
}

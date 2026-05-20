using System.Text.Json.Serialization;

namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Represents a stored event in the Memory Emulator, matching the AWS SDK wire format.
/// </summary>
public class EventModel
{
    /// <summary>
    /// Unique identifier assigned to this event on creation.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// The memory store this event belongs to.
    /// </summary>
    public string MemoryId { get; set; } = string.Empty;

    /// <summary>
    /// The actor (user or agent) that generated this event.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// The session this event belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Event timestamp as Unix epoch seconds (matches AWS SDK wire format).
    /// </summary>
    [JsonPropertyName("eventTimestamp")]
    public double EventTimestamp { get; set; }

    /// <summary>
    /// The event payload. Null when the ListEvents request specifies includePayloads=false.
    /// </summary>
    public List<PayloadTypeModel>? Payload { get; set; }
}

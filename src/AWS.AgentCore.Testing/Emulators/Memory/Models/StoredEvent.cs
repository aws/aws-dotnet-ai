namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Internal storage representation of a memory event.
/// Uses DateTime for timestamp (converted to/from Unix epoch on API boundaries).
/// </summary>
internal class StoredEvent
{
    /// <summary>
    /// Unique identifier assigned on creation.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// The memory store this event belongs to.
    /// </summary>
    public string MemoryId { get; set; } = string.Empty;

    /// <summary>
    /// The actor that generated this event.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// The session this event belongs to.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when this event occurred.
    /// </summary>
    public DateTime EventTimestamp { get; set; }

    /// <summary>
    /// The event payload containing conversational data.
    /// </summary>
    public List<PayloadTypeModel> Payload { get; set; } = [];
}

namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Response body returned by the CreateEvent API endpoint.
/// </summary>
public class CreateEventApiResponse
{
    /// <summary>
    /// The stored event with its assigned EventId and resolved timestamp.
    /// </summary>
    public EventModel Event { get; set; } = new();
}

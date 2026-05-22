namespace AWS.AgentCore.Testing.Emulators.Memory.Models;

/// <summary>
/// Response body returned by the ListEvents API endpoint.
/// </summary>
public class ListEventsApiResponse
{
    /// <summary>
    /// The list of events matching the query, in chronological order.
    /// </summary>
    public List<EventModel> Events { get; set; } = [];

    /// <summary>
    /// Pagination token for retrieving the next page of results. Null when no more pages exist.
    /// </summary>
    public string? NextToken { get; set; }
}

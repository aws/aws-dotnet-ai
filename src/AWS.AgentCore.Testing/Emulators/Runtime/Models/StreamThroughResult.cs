namespace AWS.AgentCore.Testing.Emulators.Runtime.Models;

/// <summary>
/// Result for stream-through invocations where the raw response from the agent
/// is passed directly to the caller. Implements <see cref="IAsyncDisposable"/> to
/// ensure the underlying <see cref="HttpResponseMessage"/> and stream are released
/// after the response is fully copied.
/// </summary>
public sealed class StreamThroughResult : IAsyncDisposable
{
    /// <summary>The session identifier used for this invocation.</summary>
    public string SessionId { get; }

    /// <summary>The HTTP status code returned by the agent.</summary>
    public int StatusCode { get; }

    /// <summary>The Content-Type header returned by the agent.</summary>
    public string? ContentType { get; }

    /// <summary>The raw response stream from the agent to pipe to the client.</summary>
    public Stream ResponseStream { get; }

    private readonly HttpResponseMessage _response;

    /// <inheritdoc cref="StreamThroughResult"/>
    public StreamThroughResult(string sessionId, HttpResponseMessage response, Stream responseStream)
    {
        SessionId = sessionId;
        StatusCode = (int)response.StatusCode;
        ContentType = response.Content.Headers.ContentType?.MediaType;
        ResponseStream = responseStream;
        _response = response;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await ResponseStream.DisposeAsync();
        _response.Dispose();
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using AWS.AgentCore.Hosting.Internal;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AWS.AgentCore.Hosting;

/// <summary>
/// A <see cref="ChatHistoryProvider"/> that persists conversation history to
/// Amazon Bedrock AgentCore Memory. Loads history before each agent run via ListEvents
/// and saves new messages after via CreateEvent.
/// <para>
/// Registered automatically by <c>AddAgentCore</c>.
/// Operates in pass-through mode (no-op) when MemoryId is not configured.
/// </para>
/// </summary>
internal sealed class AgentCoreMemoryProvider(
    AgentCoreOptions options,
    ILogger<AgentCoreMemoryProvider> logger,
    IAmazonBedrockAgentCore? memoryClient = null)
    : ChatHistoryProvider
{
    /// <inheritdoc/>
    public override IReadOnlyList<string> StateKeys => ["AgentCore.Memory"];

    /// <summary>
    /// Tracks the last <see cref="DateTime"/> stamped on an event per session so that
    /// timestamps issued within a single session are strictly increasing. AgentCore
    /// Memory replays events ordered by <c>EventTimestamp</c>, and <see cref="DateTime.UtcNow"/>
    /// has coarse resolution and is not monotonic, so two messages saved in the same
    /// <see cref="StoreChatHistoryAsync"/> loop (a user turn and the assistant reply)
    /// can otherwise receive the same timestamp and be replayed out of order.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _lastEventTicks = new();

    /// <summary>
    /// Returns a strictly-increasing UTC timestamp for the given session. If the wall clock
    /// has not advanced past the previously issued timestamp for the session (equal or, due to
    /// clock skew, earlier), the returned value is nudged one tick beyond the last one so that
    /// events saved back-to-back always sort deterministically on replay.
    /// </summary>
    internal DateTime NextEventTimestamp(string sessionId, DateTime now)
    {
        var ticks = _lastEventTicks.AddOrUpdate(
            sessionId,
            now.Ticks,
            (_, previous) => Math.Max(now.Ticks, previous + 1));

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Resolves the effective MemoryId from options or environment variable.
    /// Options take precedence over environment variable.
    /// </summary>
    internal string? GetEffectiveMemoryId()
    {
        if (!string.IsNullOrWhiteSpace(options.MemoryId))
            return options.MemoryId;

        var envValue = Environment.GetEnvironmentVariable(Constants.MemoryIdEnvironmentVariable);
        return string.IsNullOrWhiteSpace(envValue) ? null : envValue;
    }

    /// <summary>
    /// Retrieves the SessionId from the AgentCoreRuntimeContext stored in the session StateBag.
    /// Falls back to the ambient AsyncLocal context set by the endpoint handlers.
    /// </summary>
    internal static string? GetSessionId(AgentSession? session)
    {
        // First try the session StateBag (explicit storage by user)
        if (session is not null)
        {
            var context = session.StateBag.GetValue<AgentCoreRuntimeContext>(AgentCoreRuntimeContextProvider.ContextKey);
            if (context?.SessionId is not null)
                return context.SessionId;
        }

        // Fall back to the ambient AsyncLocal context (set automatically by MapAgentCore endpoints)
        return AgentCoreRuntimeContextProvider.CurrentContext?.SessionId;
    }

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var memoryId = GetEffectiveMemoryId();
        if (memoryId is null)
        {
            logger.LogDebug("Memory not configured — no MemoryId set via options or environment variable");
            return [];
        }

        if (memoryClient is null)
        {
            logger.LogError("MemoryId is configured but IAmazonBedrockAgentCore is not registered in DI. Memory operations will be skipped.");
            return [];
        }

        var sessionId = GetSessionId(context.Session);
        if (sessionId is null)
        {
            logger.LogWarning("SessionId not available. Ensure the request is handled by a MapAgentCore endpoint. Skipping memory load.");
            return [];
        }

        logger.LogDebug("Loading conversation history for session");

        try
        {
            var history = await LoadHistoryAsync(memoryId, sessionId, cancellationToken);
            logger.LogDebug("Loaded {MessageCount} messages from memory", history.Count());
            return history;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load conversation history from AgentCore Memory. Proceeding without history.");
            return [];
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var memoryId = GetEffectiveMemoryId();
        if (memoryId is null)
            return;

        if (memoryClient is null)
            return;

        var sessionId = GetSessionId(context.Session);
        if (sessionId is null)
            return;

        var messagesToSave = FilterMessagesForStorage(context.RequestMessages, context.ResponseMessages);

        foreach (var (role, text) in messagesToSave)
        {
            try
            {
                await SaveEventAsync(memoryId, sessionId, role, text, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save message to AgentCore Memory. Continuing.");
            }
        }
    }

    private async Task<IEnumerable<ChatMessage>> LoadHistoryAsync(
        string memoryId, string sessionId, CancellationToken cancellationToken)
    {
        if (memoryClient is null)
            return [];

        AgentCoreMetrics.RecordMemoryLoad();

        var events = new List<Event>();

        var request = new ListEventsRequest
        {
            MemoryId = memoryId,
            // ActorId = sessionId: see SaveEventAsync comment for rationale
            ActorId = sessionId,
            SessionId = sessionId,
            IncludePayloads = true
        };

        try
        {
            await foreach (var evt in memoryClient.Paginators.ListEvents(request).Events.WithCancellation(cancellationToken))
            {
                events.Add(evt);
            }
        }
        catch (Exception ex) when (events.Count > 0)
        {
            // Partial pagination failure — proceed with what we have.
            logger.LogWarning(ex, "Error during pagination. Proceeding with {Count} events loaded so far.", events.Count);
        }

        // ListEvents returns events newest-first, but chat history must be presented
        // to the model oldest-first (chronological order) so multi-turn follow-ups
        // (e.g. "prices please") bind to the most recent turn rather than a stale one.
        // OrderBy is a stable sort, so events sharing a timestamp keep their relative order.
        var messages = new List<ChatMessage>(events.Count);
        foreach (var evt in events.OrderBy(e => e.EventTimestamp))
        {
            if (TryConvertEventToChatMessage(evt, out var chatMessage))
            {
                messages.Add(chatMessage);
            }
        }

        return messages;
    }

    internal static bool TryConvertEventToChatMessage(Event evt, out ChatMessage message)
    {
        message = default!;

        if (evt.Payload is null || evt.Payload.Count == 0)
            return false;

        foreach (var payload in evt.Payload)
        {
            if (payload.Conversational is { } conversational
                && conversational.Content?.Text is { Length: > 0 } text)
            {
                ChatRole? role = null;
                if (conversational.Role == Role.USER)
                    role = ChatRole.User;
                else if (conversational.Role == Role.ASSISTANT)
                    role = ChatRole.Assistant;

                if (role is not null)
                {
                    message = new ChatMessage(role.Value, text);
                    return true;
                }
            }
        }

        return false;
    }

    private async Task SaveEventAsync(
        string memoryId, string sessionId, Role role, string text,
        CancellationToken cancellationToken)
    {
        if (memoryClient is null)
            return;

        AgentCoreMetrics.RecordMemorySave();

        await memoryClient.CreateEventAsync(new CreateEventRequest
        {
            MemoryId = memoryId,
            SessionId = sessionId,
            // NOTE: ActorId is set to sessionId intentionally. In this session-scoped short-term
            // memory implementation, the session IS the actor scope. A future long-term memory
            // feature may introduce a separate ActorId/UserId concept.
            ActorId = sessionId,
            EventTimestamp = NextEventTimestamp(sessionId, DateTime.UtcNow),
            Payload = [
                new PayloadType
                {
                    Conversational = new Conversational
                    {
                        Role = role,
                        Content = new Content { Text = text }
                    }
                }
            ]
        }, cancellationToken);
    }

    internal static IEnumerable<(Role Role, string Text)> FilterMessagesForStorage(
        IEnumerable<ChatMessage>? requestMessages,
        IEnumerable<ChatMessage>? responseMessages)
    {
        var allMessages = (requestMessages ?? []).Concat(responseMessages ?? []);

        foreach (var message in allMessages)
        {
            // Skip messages with tool-call or tool-result content
            if (HasToolContent(message))
                continue;

            // Only persist User and Assistant messages — skip System, Tool, and any other roles
            Role role;
            if (message.Role == ChatRole.User)
                role = Role.USER;
            else if (message.Role == ChatRole.Assistant)
                role = Role.ASSISTANT;
            else
                continue;

            // Extract text content
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            yield return (role, text);
        }
    }

    internal static bool HasToolContent(ChatMessage message)
    {
        if (message.Contents is null)
            return false;

        foreach (var content in message.Contents)
        {
            if (content is FunctionCallContent or FunctionResultContent)
                return true;
        }

        return false;
    }
}

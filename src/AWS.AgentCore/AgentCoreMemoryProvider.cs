// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AWS.AgentCore;

/// <summary>
/// A <see cref="ChatHistoryProvider"/> that persists conversation history to
/// Amazon Bedrock AgentCore Memory. Loads history before each agent run via ListEvents
/// and saves new messages after via CreateEvent.
/// <para>
/// Registered automatically by <see cref="Extensions.AgentCoreBuilderExtensions.AddAgentCore"/>.
/// Operates in pass-through mode (no-op) when MemoryId is not configured.
/// </para>
/// </summary>
public sealed class AgentCoreMemoryProvider(
    AgentCoreOptions options,
    ILogger<AgentCoreMemoryProvider> logger,
    IAmazonBedrockAgentCore? memoryClient = null)
    : ChatHistoryProvider
{
    /// <inheritdoc/>
    public override IReadOnlyList<string> StateKeys => ["AgentCore.Memory"];

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
        return AgentCoreRuntimeContextProvider.Current?.SessionId;
    }

    /// <inheritdoc/>
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var memoryId = GetEffectiveMemoryId();
        if (memoryId is null)
            return [];

        if (memoryClient is null)
        {
            logger.LogError("MemoryId is configured but IAmazonBedrockAgentCore is not registered in DI. Memory operations will be skipped.");
            return [];
        }

        var sessionId = GetSessionId(context.Session);
        if (sessionId is null)
        {
            logger.LogWarning("SessionId not available in session StateBag. Skipping memory load.");
            return [];
        }

        try
        {
            return await LoadHistoryAsync(memoryId, sessionId, cancellationToken);
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

        var messages = new List<ChatMessage>();
        string? nextToken = null;

        do
        {
            ListEventsResponse response;
            try
            {
                response = await memoryClient.ListEventsAsync(new ListEventsRequest
                {
                    MemoryId = memoryId,
                    ActorId = sessionId,
                    SessionId = sessionId,
                    IncludePayloads = true,
                    NextToken = nextToken
                }, cancellationToken);
            }
            catch (Exception ex) when (nextToken is not null)
            {
                // Partial pagination failure — return what we have
                logger.LogWarning(ex, "Error fetching page during pagination. Returning {Count} messages loaded so far.", messages.Count);
                break;
            }

            foreach (var evt in response.Events ?? [])
            {
                if (TryConvertEventToChatMessage(evt, out var chatMessage))
                {
                    messages.Add(chatMessage);
                }
            }

            nextToken = response.NextToken;
        }
        while (!string.IsNullOrEmpty(nextToken));

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

        await memoryClient.CreateEventAsync(new CreateEventRequest
        {
            MemoryId = memoryId,
            SessionId = sessionId,
            ActorId = sessionId,
            EventTimestamp = DateTime.UtcNow,
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

            // Extract text content
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            // Map role
            var role = message.Role == ChatRole.User
                ? Role.USER
                : Role.ASSISTANT;

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

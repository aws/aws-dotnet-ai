# Implementation Plan: AgentCore Memory Integration

## Overview

This plan implements the AgentCore Memory integration as a `ChatHistoryProvider` within the Microsoft Agent Framework pipeline. The work adds the `AWSSDK.BedrockAgentCore` package, creates `AgentCoreMemoryProvider`, modifies `AddAgentCore()` to register the Memory client and provider, and adds comprehensive unit and property-based tests. All code is C#/.NET 10.

## Tasks

- [x] 1. Add MemoryId property to AgentCoreOptions
  - [x] 1.1 Update AgentCoreOptions with MemoryId property
    - Add `public string? MemoryId { get; set; }` property to `AgentCoreOptions`
    - Add XML doc comment explaining it enables persistent conversation history and falls back to `MEMORY_ID` environment variable
    - _Requirements: 7.1, 7.2, 7.3_

- [x] 2. Add AWSSDK.BedrockAgentCore package reference
  - [x] 2.1 Update AWS.AgentCore.csproj with new package reference
    - Add `<PackageReference Include="AWSSDK.BedrockAgentCore" Version="4.0.*" />` to the ItemGroup
    - Verify the project builds successfully with the new dependency
    - _Requirements: 5.1, 9.3_

- [x] 3. Implement AgentCoreMemoryProvider class
  - [x] 3.1 Create AgentCoreMemoryProvider with constructor and configuration resolution
    - Create new file `src/AWS.AgentCore/AgentCoreMemoryProvider.cs`
    - Inherit from `ChatHistoryProvider` (from `Microsoft.Agents.AI`)
    - Accept `AgentCoreOptions`, `ILogger<AgentCoreMemoryProvider>`, and optional `IAmazonBedrockAgentCore?` via constructor
    - Implement `GetEffectiveMemoryId()` method: options takes precedence over `MEMORY_ID` environment variable
    - Implement `GetSessionId(AgentSession)` method: retrieve `AgentCoreRuntimeContext` from session StateBag using `AgentCoreRuntimeContextProvider.ContextKey`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 6.1_

  - [x] 3.2 Implement ProvideChatHistoryAsync (load history)
    - Override `ProvideChatHistoryAsync` to load conversation history from AgentCore Memory
    - Return empty collection when MemoryId is not configured (pass-through mode)
    - Return empty collection and log error when `IAmazonBedrockAgentCore` is null but MemoryId is configured
    - Return empty collection and log warning when SessionId is not available in StateBag
    - Call `ListEventsAsync` with memoryId, sessionId as actorId, sessionId, and includePayloads=true
    - Handle pagination: loop until no NextToken is returned
    - On partial pagination failure (error on page K > 1): log warning, return pages loaded so far
    - On complete failure: log error, return empty collection
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 4.1, 4.2, 4.3, 10.1, 10.2, 10.3, 10.4_

  - [x] 3.3 Implement event-to-ChatMessage conversion
    - Implement `TryConvertEventToChatMessage` static method
    - Map `ConversationRole.USER` to `ChatRole.User` and `ConversationRole.ASSISTANT` to `ChatRole.Assistant`
    - Skip events with null/empty payload, non-conversational payloads, or empty text content
    - Skip events with TOOL or OTHER roles
    - _Requirements: 1.2, 3.3_

  - [x] 3.4 Implement StoreChatHistoryAsync (save messages)
    - Override `StoreChatHistoryAsync` to persist new messages to AgentCore Memory
    - Return immediately (no-op) when MemoryId is not configured
    - Return immediately when `IAmazonBedrockAgentCore` is null or SessionId is missing
    - Filter messages: skip tool-call content (`FunctionCallContent`), tool-result content (`FunctionResultContent`), and empty/whitespace text
    - Call `CreateEventAsync` for each valid message with memoryId, sessionId, actorId=sessionId, and conversational payload
    - On failure for individual message: log error, continue with remaining messages
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.4_

  - [x] 3.5 Implement message filtering logic
    - Implement `FilterMessagesForStorage` static method
    - Implement `HasToolContent` static method to detect `FunctionCallContent` or `FunctionResultContent`
    - Only persist messages where role is User or Assistant, no tool content, and text is non-null/non-whitespace with length ≥ 1
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 4. Modify AddAgentCore() to register Memory services
  - [x] 4.1 Register IAmazonBedrockAgentCore and AgentCoreMemoryProvider in DI
    - Add `builder.Services.TryAddAWSService<IAmazonBedrockAgentCore>()` to `AddAgentCore()`
    - Add singleton registration for `AgentCoreMemoryProvider`
    - Update the `AIAgent` factory to wire `AgentCoreMemoryProvider` as a `ChatHistoryProvider` on the agent options
    - Ensure `AgentCoreMemoryProvider` executes after `AgentCoreRuntimeContextProvider` in the pipeline
    - Preserve any user-registered `AIContextProviders`
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 9.3, 9.4_

- [x] 5. Checkpoint - Verify core library compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Write unit tests for AgentCoreMemoryProvider
  - [x] 6.1 Create unit tests for pass-through and configuration behavior
    - Create new test file `test/AWS.AgentCore.UnitTests/AgentCoreMemoryProviderTests.cs`
    - Add `AWSSDK.BedrockAgentCore` package reference to the unit test project
    - Test: `ProvideChatHistory_NoMemoryId_ReturnsEmpty` — verify pass-through when MemoryId not configured
    - Test: `ProvideChatHistory_NoSessionId_ReturnsEmptyAndLogsWarning` — verify warning logged
    - Test: `ProvideChatHistory_NoMemoryClient_LogsErrorReturnsEmpty` — verify error logged when client missing
    - Test: `StoreChatHistory_NoMemoryId_DoesNothing` — verify no API calls in pass-through mode
    - Test: `GetEffectiveMemoryId_OptionsOverridesEnvVar` — verify options takes precedence
    - Test: `GetEffectiveMemoryId_FallsBackToEnvVar` — verify environment variable fallback
    - _Requirements: 4.1, 4.2, 4.3, 7.1, 7.2, 7.3, 9.1, 9.2_

  - [x] 6.2 Create unit tests for message filtering and saving
    - Test: `StoreChatHistory_SkipsToolCallMessages` — verify FunctionCallContent messages excluded
    - Test: `StoreChatHistory_SkipsToolResultMessages` — verify FunctionResultContent messages excluded
    - Test: `StoreChatHistory_SkipsEmptyTextMessages` — verify empty/whitespace text excluded
    - Test: `StoreChatHistory_SavesUserAndAssistantMessages` — verify happy path save with correct roles
    - Test: `StoreChatHistory_UsesSessionIdAsActorId` — verify actorId matches sessionId
    - Test: `StoreChatHistory_ContinuesOnIndividualFailure` — verify one failure doesn't stop others
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.4, 6.3_

  - [x] 6.3 Create unit tests for DI registration
    - Test: `AddAgentCore_RegistersMemoryProvider` — verify AgentCoreMemoryProvider is in DI
    - Test: `AddAgentCore_RegistersIAmazonBedrockAgentCore` — verify TryAddAWSService registers client
    - Test: `AddAgentCore_MemoryProviderAfterRuntimeContextProvider` — verify provider ordering
    - Test: `AddAgentCore_PreservesUserContextProviders` — verify user providers not overwritten
    - _Requirements: 5.1, 5.4, 5.5, 9.3, 9.4_

- [x] 7. Checkpoint - Ensure unit tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Write property-based tests for correctness properties
  - [x] 8.1 Write property test for event-to-ChatMessage conversion (Property 1)
    - **Property 1: Event-to-ChatMessage Conversion Preserves Data**
    - Generate arbitrary valid AgentCore Memory events with USER/ASSISTANT roles and non-empty text
    - Verify conversion produces ChatMessage with corresponding ChatRole and identical text content
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 1.2, 3.3**

  - [x] 8.2 Write property test for message filtering (Property 2)
    - **Property 2: Message Filtering Excludes Invalid Messages**
    - Generate arbitrary ChatMessages with various roles, content types, and text values
    - Verify a message is persisted if and only if: (a) User or Assistant role, (b) no FunctionCallContent/FunctionResultContent, (c) non-null non-whitespace text with length ≥ 1
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 2.3, 3.1, 3.2, 3.4**

  - [x] 8.3 Write property test for pagination (Property 3)
    - **Property 3: Pagination Fetches All Pages in Order**
    - Generate arbitrary sequences of N paginated ListEvents responses (1..N-1 have NextToken, N does not)
    - Verify provider makes exactly N API calls and returns all events concatenated in page order
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 1.3, 10.1, 10.2**

  - [x] 8.4 Write property test for error handling (Property 4)
    - **Property 4: Errors Never Propagate to Caller**
    - Generate arbitrary exceptions thrown by the Memory client during ListEvents or CreateEvent
    - Verify the provider catches the exception and returns gracefully (empty for load, no-throw for save)
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 1.5, 2.4**

  - [x] 8.5 Write property test for partial pagination failure (Property 6)
    - **Property 6: Partial Pagination Failure Returns Loaded Pages**
    - Generate pagination sequences where an error occurs on page K (K > 1)
    - Verify provider returns all events from pages 1 through K-1 and does not throw
    - Use FsCheck.Xunit with minimum 100 iterations
    - **Validates: Requirements 10.3**

- [x] 9. Verify NativeAOT compatibility
  - [x] 9.1 Verify NativeAotAnnotations sample compiles with Memory provider
    - Build the `NativeAotAnnotations` sample with `dotnet publish -c Release` (it has PublishAot=true)
    - Verify no new trimming warnings related to `AgentCoreMemoryProvider` or `IAmazonBedrockAgentCore` registration
    - Verify the Memory provider's DI registration does not use reflection-based service resolution
    - If warnings appear, add appropriate attributes or use source-generated JSON serialization
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

- [x] 10. Verify source generator compatibility
  - [x] 10.1 Verify source generator works with Memory provider registration
    - Build the `AnnotationsSample` project and verify generated code still compiles
    - Verify that `AgentCoreMemoryProvider` is available in DI when using `[AgentCoreStartup]` approach
    - Run existing source generator snapshot tests to confirm no regressions
    - Verify that `[AgentCoreHandler]`-only approach (no `[AgentCoreStartup]`) registers Memory provider in pass-through mode
    - _Requirements: 5.6, 5.7_

- [x] 11. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Property tests use FsCheck.Xunit and validate universal correctness properties from the design
- The Memory provider uses `TryAddAWSService<IAmazonBedrockAgentCore>()` so it doesn't fail if the client is already registered or not resolvable
- The provider accepts `IAmazonBedrockAgentCore?` as optional — gracefully degrades when null
- Concurrent request isolation is achieved through per-request session state (SessionId from StateBag), not shared mutable state
- The `ChatHistoryProvider` base class provides the pipeline integration points (`ProvideChatHistoryAsync` and `StoreChatHistoryAsync`)

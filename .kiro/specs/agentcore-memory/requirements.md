# Requirements Document

## Introduction

Integration of the Amazon Bedrock AgentCore Memory service into the AWS.AgentCore .NET library as an `AIContextProvider` within the Microsoft Agent Framework pipeline. This feature provides persistent conversation history that survives container restarts and scaling events, enabling stateful multi-turn conversations for agents deployed to AgentCore Runtime. The integration is opt-in (activated only when a Memory ID is configured), gracefully degrades when Memory is unavailable, and works with both the source generator and extension method developer experiences.

## Glossary

- **AgentCore_Memory**: The Amazon Bedrock AgentCore managed service for persistent conversation history, accessed via AWS SDK operations (ListEvents, CreateEvent).
- **Memory_Provider**: The AIContextProvider implementation that bridges AgentCore Memory into the MS AF pipeline, loading history before agent runs and saving new messages after.
- **MemoryId**: A unique identifier for a memory store, configured per runtime via the `MEMORY_ID` environment variable or AgentCoreOptions.
- **SessionId**: The session identifier from the AgentCore Runtime HTTP header (`X-Amzn-Bedrock-AgentCore-Runtime-Session-Id`), used to scope memory operations.
- **ActorId**: An identifier for the actor performing memory operations, derived from the session context.
- **Event**: A single entry in AgentCore Memory, containing a Conversational payload with Role (USER or ASSISTANT) and Content (text).
- **ListEvents**: The AgentCore Memory API operation that retrieves conversation history, supporting pagination via NextToken.
- **CreateEvent**: The AgentCore Memory API operation that persists a new conversation event.
- **AIContextProvider**: The MS AF abstraction for injecting context into the agent pipeline, with InvokingAsync (before) and InvokedAsync (after) hooks.
- **ChatClientAgent**: The MS AF agent type registered in DI by AddAgentCore, which executes the agent pipeline including context providers.
- **AgentCoreRuntimeContext**: The typed object populated from AgentCore HTTP headers, stored in the session StateBag.
- **AddAgentCore**: The WebApplicationBuilder extension method that registers AgentCore services in DI.
- **StateBag**: The MS AF session state dictionary where AgentCoreRuntimeContext is stored by AgentCoreSessionFactory.

## Requirements

### Requirement 1: Load Conversation History Before Agent Execution

**User Story:** As a .NET developer, I want the agent to automatically load previous conversation history from AgentCore Memory before each run, so that the agent has full context of prior interactions without manual history management.

#### Acceptance Criteria

1. WHEN the agent pipeline executes InvokingAsync and a MemoryId is configured, THE Memory_Provider SHALL call ListEvents on the AgentCore Memory service using the current SessionId and MemoryId.
2. WHEN ListEvents returns conversation events, THE Memory_Provider SHALL convert each event into a ChatMessage with the appropriate role (User or Assistant) and include them in the returned message collection.
3. WHEN ListEvents returns paginated results with a NextToken, THE Memory_Provider SHALL continue fetching subsequent pages until all history is retrieved.
4. WHEN the SessionId is not available in the session StateBag, THE Memory_Provider SHALL return an empty message collection and log a warning.
5. WHEN the AgentCore Memory service returns an error during history loading, THE Memory_Provider SHALL log the error and return an empty message collection, allowing the agent to proceed without history.

### Requirement 2: Save New Messages After Agent Execution

**User Story:** As a .NET developer, I want the agent to automatically persist new conversation messages to AgentCore Memory after each run, so that future invocations have access to the complete conversation history.

#### Acceptance Criteria

1. WHEN the agent pipeline executes InvokedAsync and a MemoryId is configured, THE Memory_Provider SHALL call CreateEvent on the AgentCore Memory service for the user input message.
2. WHEN the agent pipeline executes InvokedAsync and a MemoryId is configured, THE Memory_Provider SHALL call CreateEvent on the AgentCore Memory service for the assistant response message.
3. WHEN a message has empty or whitespace-only text content, THE Memory_Provider SHALL skip that message and not call CreateEvent for it.
4. WHEN the AgentCore Memory service returns an error during message saving, THE Memory_Provider SHALL log the error and allow the agent response to proceed without interruption.
5. THE Memory_Provider SHALL use the same SessionId and MemoryId for CreateEvent as was used for ListEvents in the same invocation.

### Requirement 3: Filter Tool-Call Messages

**User Story:** As a .NET developer, I want tool-call and tool-result messages to be excluded from Memory persistence, so that only human-readable conversation content is stored and the Memory service does not reject messages with empty text.

#### Acceptance Criteria

1. WHEN saving messages to AgentCore Memory, THE Memory_Provider SHALL exclude messages that contain tool-call content (function invocations).
2. WHEN saving messages to AgentCore Memory, THE Memory_Provider SHALL exclude messages that contain tool-result content (function responses).
3. WHEN loading history from AgentCore Memory, THE Memory_Provider SHALL only produce ChatMessages with User or Assistant roles containing text content.
4. THE Memory_Provider SHALL only persist messages where the text content has a length of at least 1 character.

### Requirement 4: Graceful Degradation Without Memory Configuration

**User Story:** As a .NET developer, I want my agent to work statelessly when AgentCore Memory is not configured, so that I can develop and test locally without a Memory service dependency.

#### Acceptance Criteria

1. WHEN MemoryId is not configured (neither in AgentCoreOptions nor the MEMORY_ID environment variable), THE Memory_Provider SHALL skip all Memory operations and return empty results from InvokingAsync.
2. WHEN MemoryId is not configured, THE Memory_Provider SHALL not call any AgentCore Memory API operations.
3. WHEN MemoryId is not configured, THE Memory_Provider SHALL not log errors or warnings about missing configuration.
4. WHEN MemoryId becomes available after initial startup (configuration change), THE Memory_Provider SHALL use the configured MemoryId for subsequent requests.

### Requirement 5: Automatic Registration via AddAgentCore

**User Story:** As a .NET developer, I want the Memory provider to be automatically registered when I call AddAgentCore, so that I get persistent conversation history without additional setup code.

#### Acceptance Criteria

1. WHEN AddAgentCore is called, THE AddAgentCore method SHALL always register the Memory_Provider as an AIContextProvider in the DI container.
2. WHEN a MemoryId is available (via AgentCoreOptions or MEMORY_ID environment variable), THE Memory_Provider SHALL actively load and save conversation history.
3. WHEN no MemoryId is available, THE Memory_Provider SHALL operate in pass-through mode without calling any Memory APIs.
4. THE Memory_Provider registration SHALL not interfere with other AIContextProviders registered by the user.
5. THE Memory_Provider SHALL execute after the AgentCoreRuntimeContextProvider in the pipeline, ensuring the SessionId is available in the StateBag.
6. WHEN using the source generator approach with [AgentCoreStartup] and ConfigureServices calling AddAgentCore, THE Memory_Provider SHALL be registered with the same behavior as the extension method approach.
7. WHEN using the source generator approach with [AgentCoreHandler] only (no [AgentCoreStartup]), THE generated code SHALL call AddAgentCore with default options, which registers the Memory_Provider in pass-through mode (activating via MEMORY_ID environment variable at runtime).

### Requirement 6: Use Session ID from AgentCoreRuntimeContext

**User Story:** As a .NET developer, I want the Memory provider to automatically use the session ID from AgentCore HTTP headers, so that conversation history is correctly scoped per session without manual session management.

#### Acceptance Criteria

1. WHEN the Memory_Provider executes, THE Memory_Provider SHALL retrieve the SessionId from the AgentCoreRuntimeContext stored in the session StateBag.
2. WHEN multiple concurrent requests arrive with different session IDs, THE Memory_Provider SHALL use the correct SessionId for each request's Memory operations.
3. THE Memory_Provider SHALL use the SessionId as the ActorId for CreateEvent operations.
4. WHEN the SessionId changes between invocations (new session), THE Memory_Provider SHALL load history for the new SessionId.

### Requirement 7: MemoryId Configuration

**User Story:** As a .NET developer, I want to configure the Memory ID via options or environment variable, so that I can use different memory stores for different environments.

#### Acceptance Criteria

1. WHEN the MEMORY_ID environment variable is set, THE Memory_Provider SHALL use that value as the MemoryId for all Memory operations.
2. WHEN a MemoryId is set in AgentCoreOptions, THE AgentCoreOptions value SHALL take precedence over the MEMORY_ID environment variable.
3. WHEN neither AgentCoreOptions.MemoryId nor the MEMORY_ID environment variable is set, THE Memory_Provider SHALL operate in pass-through mode without calling Memory APIs.
4. THE MemoryId configuration SHALL be readable at request time, allowing runtime configuration changes.

### Requirement 8: Concurrent Request Isolation

**User Story:** As a .NET developer deploying to AgentCore Runtime, I want concurrent requests with different sessions to have isolated memory operations, so that conversation histories do not leak between users.

#### Acceptance Criteria

1. WHEN two concurrent requests arrive with different SessionIds, THE Memory_Provider SHALL load independent conversation histories for each request.
2. WHEN two concurrent requests arrive with different SessionIds, THE Memory_Provider SHALL save messages to the correct session's history independently.
3. THE Memory_Provider SHALL not use shared mutable state between concurrent requests.
4. WHEN a Memory operation for one request fails, THE failure SHALL not affect Memory operations for other concurrent requests.

### Requirement 9: Non-Interference with Existing Agents

**User Story:** As an existing AWS.AgentCore user who does not use Memory, I want the Memory integration to have no impact on my agent's behavior or performance, so that I can upgrade without risk.

#### Acceptance Criteria

1. WHEN MemoryId is not configured, THE Memory_Provider SHALL add no measurable latency to agent invocations.
2. WHEN MemoryId is not configured, THE Memory_Provider SHALL not make any network calls.
3. THE Memory_Provider registration SHALL not require any new mandatory dependencies in the DI container.
4. WHEN the AWS SDK client for AgentCore Memory is not available and MemoryId is configured, THE Memory_Provider SHALL log an error and operate in pass-through mode.

### Requirement 10: Pagination Handling for Long Conversations

**User Story:** As a .NET developer building conversational agents, I want the Memory provider to handle paginated history correctly, so that agents with long conversation histories load all prior context.

#### Acceptance Criteria

1. WHEN ListEvents returns a NextToken in the response, THE Memory_Provider SHALL issue subsequent ListEvents calls with the NextToken until no NextToken is returned.
2. THE Memory_Provider SHALL assemble paginated results in chronological order.
3. WHEN pagination encounters an error on a subsequent page, THE Memory_Provider SHALL return the successfully loaded pages and log a warning about incomplete history.
4. THE Memory_Provider SHALL not impose an artificial limit on the number of pages fetched.

### Requirement 11: NativeAOT Compatibility

**User Story:** As a .NET developer targeting NativeAOT, I want the Memory provider to work without reflection or dynamic code generation, so that agents compiled ahead-of-time can use persistent conversation history with fast cold starts.

#### Acceptance Criteria

1. THE Memory_Provider SHALL not use reflection-based serialization or dynamic code generation for any Memory API operations.
2. WHEN compiled with PublishAot=true, THE Memory_Provider SHALL produce no trimming warnings.
3. THE Memory_Provider's DI registration SHALL not use reflection-based service resolution.
4. WHEN the NativeAotAnnotations sample is configured with a MemoryId, THE application SHALL compile and run correctly with PublishAot=true.
5. THE Memory_Provider SHALL use source-generated JSON serialization (JsonSerializerContext) for any custom types serialized to or deserialized from the Memory API.

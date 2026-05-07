# Requirements Document

## Introduction

Deep integration of Microsoft Agent Framework 1.0 into AWS.AgentCore so that .NET developers can leverage the full MS AF pipeline (middleware, context providers, sessions, MCP, A2A, structured output, workflows) when building agents deployed to Amazon Bedrock AgentCore Runtime. The integration follows a thin-layer philosophy: AWS.AgentCore provides Bedrock as a convenient default model provider, bridges AgentCore-specific concerns (runtime context, session IDs from headers) into the MS AF pipeline, and handles the deployment layer (endpoints, streaming, port config). It does NOT wrap or mask MS AF features — users configure their agents through standard MS AF patterns.

## Glossary

- **AgentCore_Runtime**: The Amazon Bedrock AgentCore managed service that hosts and scales agent containers, communicating via POST /invocations and GET /ping.
- **MS_AF**: Microsoft Agent Framework 1.0 (GA April 2026), the unified .NET agent runtime providing AIAgent, middleware, context providers, sessions, MCP, A2A, and workflows.
- **ChatClientAgent**: The MS AF agent type that delegates to an IChatClient for LLM calls, supporting the full agent pipeline (middleware, context providers, function calling).
- **Agent_Pipeline**: The MS AF execution pipeline: Agent Middleware → Context Providers → Chat Client (with its own middleware).
- **IChatClient**: The Microsoft.Extensions.AI abstraction for chat-based LLM interactions, implemented by various providers (Bedrock, OpenAI, Anthropic, etc.).
- **AgentCoreRuntimeContext**: The AWS.AgentCore typed object populated from AgentCore HTTP headers (SessionId, RequestId, AccessToken, OAuth2CallbackUrl, CustomHeaders).
- **AddAgentCore**: The existing WebApplicationBuilder extension method that registers AgentCore services (options, port, Bedrock client).
- **MapAgentCore**: The existing endpoint extension method that maps POST /invocations and GET /ping routes.
- **AIContextProvider**: MS AF abstraction for injecting additional context (memory, RAG, dynamic instructions) into the agent pipeline.
- **AgentSession**: MS AF state management object for a conversation turn.
- **Source_Generator**: The AWS.AgentCore Roslyn source generator that emits Program.cs from [AgentCoreStartup], [AgentCoreHandler], [AgentCorePing] attributes.
- **NativeAOT**: Ahead-of-time compilation mode requiring no reflection or dynamic code generation.

## Requirements

### Requirement 1: Register ChatClientAgent in DI

**User Story:** As a .NET developer, I want AddAgentCore to register a fully-configured ChatClientAgent in the DI container, so that I can resolve it from DI and use the full MS AF pipeline without manual agent construction.

#### Acceptance Criteria

1. WHEN AddAgentCore is called with a configure callback, THE AddAgentCore method SHALL register a ChatClientAgent instance in the DI container as a singleton.
2. WHEN a ChatClientAgent is resolved from DI, THE ChatClientAgent SHALL be configured with the IChatClient registered in the container.
3. WHEN a user provides ChatClientAgentOptions via the configure callback, THE AddAgentCore method SHALL pass those options to the ChatClientAgent constructor.
4. WHEN no ChatClientAgentOptions are provided, THE AddAgentCore method SHALL create a ChatClientAgent with default options.
5. IF the IChatClient registration is missing from the container, THEN THE service resolution SHALL throw an InvalidOperationException with a descriptive message.

### Requirement 2: Make Bedrock Model Provider Optional

**User Story:** As a .NET developer, I want to use any IChatClient provider (OpenAI, Anthropic, Ollama) with AgentCore, so that I am not locked into Amazon Bedrock.

#### Acceptance Criteria

1. WHEN ModelId is set in AgentCoreOptions and no ChatClient is provided, THE AddAgentCore method SHALL register a Bedrock-backed IChatClient as the default.
2. WHEN a user sets the ChatClient property on AgentCoreOptions, THE AddAgentCore method SHALL register that instance as the IChatClient in the DI container, regardless of whether ModelId is also set.
3. WHEN both ChatClient and ModelId are set, THE ChatClient property SHALL take precedence over ModelId.
4. WHEN a user registers their own IChatClient in the DI container before calling AddAgentCore and neither ChatClient nor ModelId is set, THE AddAgentCore method SHALL not overwrite the existing IChatClient registration.
5. WHEN no IChatClient is available (no ChatClient property, no ModelId, no pre-registered IChatClient in DI), IF a ChatClientAgent is resolved from DI, THEN THE service resolution SHALL throw an InvalidOperationException indicating that an IChatClient must be provided via options.ChatClient, options.ModelId, or direct DI registration.

### Requirement 3: Bridge AgentCore Session ID into MS AF AgentSession

**User Story:** As a .NET developer, I want the AgentCore session ID from HTTP headers to automatically flow into the MS AF AgentSession, so that conversation continuity works without manual session management.

#### Acceptance Criteria

1. WHEN a POST /invocations request contains the X-Amzn-Bedrock-AgentCore-Runtime-Session-Id header, THE integration layer SHALL use that value as the session identifier for the MS AF AgentSession.
2. WHEN the session ID header is absent, THE integration layer SHALL generate a new unique session identifier for the AgentSession.
3. THE integration layer SHALL make the session identifier available to AIContextProviders and middleware within the agent pipeline.
4. WHEN multiple concurrent requests arrive with different session IDs, THE integration layer SHALL isolate each request's session context from other concurrent requests.

### Requirement 4: Expose AgentCoreRuntimeContext as an AIContextProvider

**User Story:** As a .NET developer, I want to access AgentCore runtime context (headers, tokens, custom headers) from within the MS AF pipeline, so that my middleware and context providers can make decisions based on AgentCore-specific information.

#### Acceptance Criteria

1. THE AddAgentCore method SHALL register an AIContextProvider that injects AgentCoreRuntimeContext data into the agent pipeline.
2. WHEN the agent pipeline executes, THE AgentCore context provider SHALL make the SessionId, RequestId, AccessToken, OAuth2CallbackUrl, and CustomHeaders available to downstream middleware and context providers.
3. WHEN a user adds additional AIContextProviders, THE AgentCore context provider SHALL not interfere with or replace user-registered context providers.
4. THE AgentCore context provider SHALL execute before user-registered context providers in the pipeline.

### Requirement 5: Support Agent Configuration via Standard MS AF Patterns

**User Story:** As a .NET developer, I want to configure my agent's tools, instructions, middleware, and context providers using standard MS AF APIs (AsBuilder().Use(), ChatClientAgentOptions), so that I can leverage MS AF documentation and patterns directly.

#### Acceptance Criteria

1. WHEN a user configures tools via ChatClientAgentOptions.ChatOptions.Tools, THE ChatClientAgent SHALL use those tools during invocation.
2. WHEN a user adds middleware via the agent builder pattern (agent.AsBuilder().Use()), THE agent pipeline SHALL execute that middleware on each invocation.
3. WHEN a user registers AIContextProviders in DI, THE agent pipeline SHALL invoke those context providers during execution.
4. WHEN a user sets Instructions in ChatClientAgentOptions, THE ChatClientAgent SHALL include those instructions in every LLM call.
5. THE integration SHALL not require users to use any AWS.AgentCore-specific API to configure MS AF features.

### Requirement 6: Support Streaming via MS AF RunStreamingAsync

**User Story:** As a .NET developer, I want to stream agent responses through the AgentCore SSE wire format while using the full MS AF pipeline, so that I get real-time responses with middleware and context providers active.

#### Acceptance Criteria

1. WHEN a MapAgentCore handler returns IAsyncEnumerable from an MS AF agent's RunStreamingAsync, THE endpoint SHALL write each chunk as an SSE event in the AgentCore wire format.
2. WHEN streaming through the MS AF pipeline, THE agent middleware SHALL execute for each invocation.
3. WHEN streaming through the MS AF pipeline, THE context providers SHALL execute before the LLM call.
4. IF an error occurs during streaming after headers are written, THEN THE endpoint SHALL emit an SSE error event rather than changing the HTTP status code.

### Requirement 7: Maintain Backward Compatibility

**User Story:** As an existing AWS.AgentCore user, I want my current code to continue working without changes after the MS AF integration is added, so that I can adopt new features incrementally.

#### Acceptance Criteria

1. WHEN existing code calls AddAgentCore with only ModelId set, THE method SHALL continue to register IChatClient and AgentCoreOptions as before.
2. WHEN existing code uses MapAgentCore with inline agent creation via chatClient.AsAIAgent(), THE endpoint SHALL continue to function correctly.
3. THE AddAgentCore method SHALL not require any new mandatory parameters.
4. THE existing MapAgentCore overloads (delegate, strongly-typed, NativeAOT) SHALL remain available and functional.
5. WHEN the MS AF integration features are not used, THE library SHALL not add measurable startup latency.

### Requirement 8: Support Source Generator Approach

**User Story:** As a .NET developer using the source generator approach, I want the MS AF integration to work with [AgentCoreStartup] and [AgentCoreHandler] attributes, so that I get the same pipeline benefits with zero Program.cs.

#### Acceptance Criteria

1. WHEN [AgentCoreStartup] is used with ConfigureServices, THE generated code SHALL register the ChatClientAgent and AgentCore context provider in DI.
2. WHEN [AgentCoreHandler] is used, THE generated endpoint code SHALL resolve the ChatClientAgent from DI and make it available to the handler.
3. WHEN a user configures ChatClientAgentOptions in ConfigureServices, THE generated code SHALL pass those options to the ChatClientAgent.
4. THE source generator approach SHALL produce equivalent runtime behavior to the extension method approach.

### Requirement 9: NativeAOT Compatibility

**User Story:** As a .NET developer targeting NativeAOT, I want the MS AF integration to work without reflection or dynamic code generation, so that my agent can be compiled ahead-of-time for fast cold starts.

#### Acceptance Criteria

1. THE ChatClientAgent registration SHALL not use reflection-based service resolution.
2. THE AgentCore context provider SHALL not use dynamic code generation.
3. WHEN compiled with PublishAot=true, THE integration SHALL produce no trimming warnings related to MS AF registration.
4. IF MS AF features require reflection (such as certain middleware patterns), THEN THE library SHALL document which features are NativeAOT-compatible and which are not.

### Requirement 10: Allow User-Controlled Agent Lifecycle

**User Story:** As a .NET developer building advanced agents, I want full control over agent construction and session lifecycle when needed, so that I can implement custom patterns (multi-agent, conditional routing, dynamic tool selection) without fighting the framework.

#### Acceptance Criteria

1. WHEN a user resolves ChatClientAgent from DI and calls CreateSessionAsync and RunAsync manually, THE integration SHALL not interfere with the user's lifecycle management.
2. WHEN a user builds a custom agent using agent.AsBuilder().Use() and registers it in DI, THE MapAgentCore convenience overloads SHALL use the user's configured agent.
3. WHEN a user needs multiple agents with different configurations, THE DI registration SHALL support named or keyed agent registrations.
4. THE integration SHALL not force a single agent instance for all requests.

### Requirement 11: Do Not Wrap MS AF Features

**User Story:** As a .NET developer, I want direct access to MS AF features (MCP, A2A, workflows, structured output) without AWS.AgentCore wrapper abstractions, so that I can follow MS AF documentation directly and benefit from new MS AF features without waiting for AWS.AgentCore updates.

#### Acceptance Criteria

1. THE library SHALL not create wrapper types around MS AF middleware, context providers, or workflow APIs.
2. WHEN Microsoft adds new features to MS AF, THE AWS.AgentCore library SHALL not require code changes to support those features.
3. THE library SHALL expose the ChatClientAgent instance directly (not behind an abstraction) so users can call any MS AF API on it.
4. WHEN a user configures MCP servers via MS AF's native MCP API, THE integration SHALL not interfere with MCP functionality.
5. WHEN a user configures A2A via MS AF's native A2A API, THE integration SHALL not interfere with A2A functionality.

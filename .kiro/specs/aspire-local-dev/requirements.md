# Requirements Document

## Introduction

A local development experience for the AWS.AgentCore .NET library using .NET Aspire that enables developers to run, test, and iterate on their AgentCore agents without requiring an AWS connection. The feature provides an Aspire AppHost that orchestrates agent applications with embedded in-process emulators for the AgentCore Runtime and Memory services, plus a ChatBot UI for interactive testing. Developers press F5/Run in their IDE and the entire local stack starts up — agent app with embedded emulators and a chat interface — with zero AWS credentials required for the core development loop. All emulators run in-process within the AppHost. The `AddAgentCoreRuntime<TProject>()` extension method returns `IResourceBuilder<ProjectResource>`, making it compatible with Aspire's deployment features (`PublishAs*`) and the `WithReference()` pattern for injecting the runtime emulator endpoint into consuming projects.

## Glossary

- **Aspire_AppHost**: The .NET Aspire orchestrator project that defines and launches agent resources. Emulators run embedded within this process.
- **Runtime_Emulator**: An embedded in-process HTTP server that emulates the AgentCore Runtime by exposing the SDK-compatible `POST /runtimes/{arn}/invocations` endpoint and forwarding to the agent's `POST /invocations`.
- **Memory_Emulator**: An embedded in-process HTTP server that implements the ListEvents and CreateEvent APIs in-memory, allowing the AgentCoreMemoryProvider to function without an AWS connection.
- **ChatBot_UI**: An embedded Blazor Server application providing a web-based chat interface with a configurable payload editor, dynamic parameters, and light/dark theme support.
- **Agent_App**: The developer's AgentCore application built with `builder.AddAgentCore()` and `app.MapAgentCore<TRequest>()`.
- **Aspire_Dashboard**: The .NET Aspire dashboard providing resource status, logs, and traces.
- **AddAgentCoreRuntime**: The `IDistributedApplicationBuilder` extension method that registers an agent with embedded emulators and ChatBot UI. Returns `IResourceBuilder<ProjectResource>`.
- **WithReference**: A custom extension method on `IResourceBuilder<ProjectResource>` that detects an AgentCore runtime annotation and injects `AGENTCORE_SERVICE_ENDPOINT` with the runtime emulator URL into the consuming project.
- **AgentCore_Runtime**: The real Amazon Bedrock AgentCore Runtime service that hosts agents in production.
- **SessionId**: The session identifier header (`X-Amzn-Bedrock-AgentCore-Runtime-Session-Id`) that the Runtime sends to scope conversations.
- **PortAllocator**: A utility that pre-allocates TCP ports for embedded servers before they start.
- **AWS_AGENTCORE_ASPIRE_MANAGED**: Environment variable set to `"true"` that tells `AddAgentCore()` to skip its default port 8080 binding.
- **AGENTCORE_SERVICE_ENDPOINT**: Environment variable injected by `WithReference(agent)` containing the runtime emulator URL. Consuming apps use this as the AWS SDK's `ServiceURL` override.
- **ChatBotUI_Sample**: A standalone Blazor Server web app (`sampleapps/ChatBotUI/`) that demonstrates how a consumer reads `AGENTCORE_SERVICE_ENDPOINT` and overrides the AWS SDK's `ServiceURL`.
- **RemoteMcpAgent_Sample**: A sample agent (`sampleapps/RemoteMcpAgent/`) that connects to remote MCP servers and registers their tools with the Microsoft Agent Framework.

## Requirements

### Requirement 1: Aspire AppHost Orchestration

**User Story:** As a .NET developer, I want an Aspire AppHost that orchestrates my agent app with embedded emulators, so that I can start the entire local development stack with a single F5/Run action.

#### Acceptance Criteria

1. WHEN the developer runs the Aspire_AppHost project, THE Aspire_AppHost SHALL start the Agent_App as a managed Aspire project resource.
2. WHEN the developer runs the Aspire_AppHost project, THE Aspire_AppHost SHALL start embedded in-process servers (Runtime Emulator, ChatBot UI, and optionally Memory Emulator) via `AfterResourcesCreatedEvent`.
3. WHEN the developer runs the Aspire_AppHost project, THE Aspire_Dashboard SHALL be available for observability (logs, resource status).
4. WHEN the developer stops the Aspire_AppHost, THE Aspire_AppHost SHALL gracefully shut down the Agent_App and all embedded servers.
5. THE Aspire_AppHost SHALL show the agent as a project resource with "Agent" and "Chat" URL links.

### Requirement 2: Local Runtime Emulator

**User Story:** As a .NET developer, I want a local Runtime emulator that sends invocation requests to my agent, so that I can test the full request/response cycle without deploying to AWS.

#### Acceptance Criteria

1. THE Runtime_Emulator SHALL expose `POST /runtimes/{agentRuntimeArn}/invocations` to accept requests from the AWS SDK.
2. THE Runtime_Emulator SHALL forward the raw JSON payload to the Agent_App's `POST /invocations` endpoint with the `X-Amzn-Bedrock-AgentCore-Runtime-Session-Id` and `X-Amzn-Bedrock-AgentCore-Runtime-Request-Id` headers.
3. THE Runtime_Emulator SHALL pass the request body through as-is without wrapping it in an additional JSON envelope.
4. THE Runtime_Emulator SHALL call GET /ping on the Agent_App to verify readiness before sending invocation requests.
5. WHEN the Agent_App returns a JSON response from a non-streaming handler, THE Runtime_Emulator SHALL pass the response through to the caller.
6. WHEN the Agent_App returns an SSE streaming response, THE Runtime_Emulator SHALL pass the SSE stream through directly to the caller.
7. THE Runtime_Emulator SHALL run as an embedded in-process server.
8. THE Runtime_Emulator SHALL support multiple concurrent sessions, each with a distinct SessionId.
9. THE Runtime_Emulator's logs SHALL be visible in the Aspire Dashboard via `ResourceLoggerService`.

### Requirement 3: Local Memory Emulator

**User Story:** As a .NET developer, I want a local Memory emulator that stores conversation history in-memory, so that the AgentCoreMemoryProvider works without an AWS connection during local development.

#### Acceptance Criteria

1. THE Memory_Emulator SHALL implement the ListEvents API endpoint, returning stored conversation events filtered by MemoryId, SessionId, and ActorId.
2. THE Memory_Emulator SHALL implement the CreateEvent API endpoint, accepting and storing conversation events with MemoryId, SessionId, ActorId, EventTimestamp, and Payload.
3. WHEN ListEvents is called with a MemoryId and SessionId that has stored events, THE Memory_Emulator SHALL return those events in chronological order.
4. WHEN ListEvents is called with a MemoryId and SessionId that has no stored events, THE Memory_Emulator SHALL return an empty event list.
5. THE Memory_Emulator SHALL store events in-memory for the lifetime of the AppHost process.
6. WHEN the AppHost is restarted, THE Memory_Emulator SHALL start with an empty event store.
7. THE Memory_Emulator SHALL support pagination by returning a NextToken when the result set exceeds a configurable page size.
8. THE Memory_Emulator SHALL expose an HTTP API compatible with the IAmazonBedrockAgentCore SDK client so that the existing AgentCoreMemoryProvider can connect without code changes.
9. THE Memory_Emulator SHALL run as an embedded in-process server.

### Requirement 4: Zero-Configuration Agent App Integration

**User Story:** As a .NET developer, I want my existing AgentCore app to work with the local emulators without code changes or complicated configuration, so that the local development experience is seamless.

#### Acceptance Criteria

1. WHEN the Agent_App is launched by the Aspire_AppHost, THE Testing_Package SHALL set `AWS_AGENTCORE_SERVICE_ENDPOINT` on the Agent_App pointing to the embedded Memory_Emulator endpoint.
2. WHEN the Agent_App is launched by the Aspire_AppHost, THE Testing_Package SHALL set `AWS_AGENTCORE_MEMORY_ID` on the Agent_App so that the AgentCoreMemoryProvider activates automatically.
3. THE Agent_App SHALL require no source code changes to run under the Aspire_AppHost compared to running in production on AgentCore Runtime.
4. WHEN the Agent_App uses `builder.AddAgentCore()` with a ModelId, THE Agent_App SHALL still require valid AWS credentials for Bedrock LLM calls unless the developer provides a mock IChatClient.
5. THE Testing_Package SHALL set `AWS_AGENTCORE_ASPIRE_MANAGED=true` on the Agent_App so that `AddAgentCore()` skips its default port 8080 binding, allowing Aspire DCP to allocate the port.
6. THE Testing_Package SHALL configure the Agent_App with `WithHttpEndpoint(name: "http")` for Aspire DCP port allocation.

### Requirement 5: Observability via Aspire Dashboard

**User Story:** As a .NET developer, I want the Aspire Dashboard available for observability, so that I can view logs and resource status during local development.

#### Acceptance Criteria

1. WHEN the Aspire_AppHost starts, THE Aspire_Dashboard SHALL be available at a local URL for the developer to view resource status and logs.
2. THE Aspire_Dashboard SHALL display console logs from the Agent_App.
3. THE Aspire_Dashboard SHALL show the Agent_App resource with "Agent" and "Chat" URL links.
4. THE embedded Runtime_Emulator's logs SHALL be piped to the Aspire Dashboard via `AspireLoggerProvider`.

### Requirement 6: Developer Prompt Submission Interface

**User Story:** As a .NET developer, I want a ChatBot UI accessible from the Aspire dashboard, so that I can quickly test agent behavior through a web interface.

#### Acceptance Criteria

1. THE ChatBot_UI SHALL be accessible via the "Chat" link on the agent's dashboard row.
2. THE ChatBot_UI SHALL allow the developer to submit prompts and view agent responses in a chat interface.
3. THE ChatBot_UI SHALL support standard JSON request/response mode for non-streaming agents.
4. WHEN `.WithStreaming()` is configured, THE ChatBot_UI SHALL support SSE streaming mode, displaying response chunks as they arrive.
5. THE ChatBot_UI SHALL manage conversation sessions, allowing the developer to start new sessions or continue existing ones.
6. THE ChatBot_UI SHALL provide a configurable payload editor that allows users to define custom JSON request shapes with `{{paramName}}` placeholders.
7. THE ChatBot_UI SHALL support dynamic parameters with types (string, number, boolean, raw JSON) that render as input fields in the chat area.
8. THE ChatBot_UI SHALL include built-in payload templates (Simple Prompt, Message+Role, Query+Parameters, Structured Input, RAG Query, Multi-turn Chat).
9. THE ChatBot_UI SHALL support light and dark themes with persistence via cookies.
10. THE ChatBot_UI SHALL display the AWS logo appropriate to the current theme.

### Requirement 7: No AWS Credentials Required for Core Loop

**User Story:** As a .NET developer, I want to run the local development stack without AWS credentials for the core agent loop, so that I can develop and test offline or without an AWS account.

#### Acceptance Criteria

1. WHEN the Agent_App is launched by the Aspire_AppHost with the Memory_Emulator configured via `.WithInMemory()`, THE Agent_App SHALL not require AWS credentials for Memory operations.
2. WHEN the Agent_App uses a mock or local IChatClient (not Bedrock), THE Agent_App SHALL not require any AWS credentials at all.
3. THE embedded Memory_Emulator SHALL not require AWS credentials to operate.
4. THE embedded Runtime_Emulator SHALL not require AWS credentials to operate.
5. WHEN the Agent_App uses `options.ModelId` for Bedrock LLM calls, THE documentation SHALL clearly state that AWS credentials are still required for LLM inference.

### Requirement 8: Sample AppHost Project

**User Story:** As a .NET developer new to AgentCore, I want a sample Aspire AppHost project that demonstrates the local development setup, so that I can quickly get started with a working example.

#### Acceptance Criteria

1. THE repository SHALL include a sample Aspire AppHost project at `sampleapps/AspireAppHost/` that references existing sample agent apps.
2. THE sample Aspire AppHost SHALL demonstrate registering multiple agents using `AddAgentCoreRuntime<TProject>()` with `.WithInMemory()` and `.WithStreaming()`.
3. THE sample Aspire AppHost SHALL demonstrate `WithReference(agent)` to wire a ChatBotUI sample app to an agent's runtime emulator.
4. WHEN the developer opens the solution and runs the AppHost project, THE entire local stack SHALL start without additional setup steps beyond having the .NET Aspire workload installed.

### Requirement 9: Memory Emulator SDK Compatibility

**User Story:** As a .NET developer, I want the Memory emulator to be compatible with the existing IAmazonBedrockAgentCore SDK interface, so that the AgentCoreMemoryProvider works without modification.

#### Acceptance Criteria

1. THE Memory_Emulator SHALL accept HTTP requests in the same format that the IAmazonBedrockAgentCore SDK client sends for ListEvents operations.
2. THE Memory_Emulator SHALL accept HTTP requests in the same format that the IAmazonBedrockAgentCore SDK client sends for CreateEvent operations.
3. THE Memory_Emulator SHALL return HTTP responses in the same format that the IAmazonBedrockAgentCore SDK client expects for ListEvents operations.
4. THE Memory_Emulator SHALL return HTTP responses in the same format that the IAmazonBedrockAgentCore SDK client expects for CreateEvent operations.
5. WHEN the Agent_App's IAmazonBedrockAgentCore SDK client is configured to point at the Memory_Emulator endpoint, THE AgentCoreMemoryProvider SHALL load and save conversation history without code changes.
6. THE Memory_Emulator SHALL handle the IncludePayloads parameter on ListEvents requests, returning payloads only when requested.

### Requirement 10: Streaming Support

**User Story:** As a .NET developer building a streaming agent, I want the local testing stack to support SSE streaming, so that I can test streaming behavior locally.

#### Acceptance Criteria

1. WHEN `.WithStreaming()` is called on the agent resource builder, THE ChatBot_UI SHALL be configured for SSE streaming mode.
2. THE Runtime_Emulator SHALL pass SSE streams through directly from the Agent_App to the caller without buffering.
3. THE ChatBot_UI SHALL display streaming response chunks as they arrive in real-time.
4. WHEN `.WithStreaming()` is NOT called, THE ChatBot_UI SHALL use standard JSON request/response mode.

### Requirement 11: Graceful Handling of Missing Optional Services

**User Story:** As a .NET developer, I want the local development stack to work even if optional components are not configured, so that I can run a minimal setup when needed.

#### Acceptance Criteria

1. WHEN `.WithInMemory()` is NOT called, THE Agent_App SHALL operate statelessly without errors (no memory emulator started).
2. WHEN only `AddAgentCoreRuntime<TProject>()` is called without any chained methods, THE Agent_App SHALL still start with a Runtime Emulator and ChatBot UI.
3. WHEN multiple agents are registered, EACH agent SHALL have its own independent set of embedded servers with distinct ports.

### Requirement 12: Aspire Deployment Compatibility

**User Story:** As a .NET developer, I want the `AddAgentCoreRuntime` extension to be compatible with Aspire's deployment features, so that I can use `PublishAs*` methods and `WithReference` without workarounds.

#### Acceptance Criteria

1. `AddAgentCoreRuntime<TProject>()` SHALL return `IResourceBuilder<ProjectResource>` so that deployment extensions like `PublishAsECSFargateService()` can be chained directly.
2. A custom `WithReference(IResourceBuilder<ProjectResource> agent)` extension SHALL detect the `AgentCoreRuntimeAnnotation` and inject `AGENTCORE_SERVICE_ENDPOINT` as an environment variable on the consuming project.
3. The `AGENTCORE_SERVICE_ENDPOINT` value SHALL be the runtime emulator's `http://localhost:{port}` URL.
4. Consuming projects (e.g., ChatBotUI) SHALL override the AWS SDK's `ServiceURL` with the `AGENTCORE_SERVICE_ENDPOINT` value to communicate through the runtime emulator.

### Requirement 13: ChatBotUI Sample App Integration

**User Story:** As a .NET developer, I want a standalone ChatBotUI sample app that can be wired to any agent via Aspire's `WithReference`, so that I can build custom frontends that work both locally and in production.

#### Acceptance Criteria

1. THE ChatBotUI sample app SHALL read the `AGENTCORE_SERVICE_ENDPOINT` environment variable at startup.
2. WHEN `AGENTCORE_SERVICE_ENDPOINT` is set, THE ChatBotUI SHALL override the AWS SDK's `ServiceURL` and use `"local-agent"` as the RuntimeArn.
3. WHEN `AGENTCORE_SERVICE_ENDPOINT` is NOT set, THE ChatBotUI SHALL use the standard AWS SDK with `RuntimeArn` and `Region` from `appsettings.json`.
4. THE ChatBotUI SHALL work with both the runtime emulator (local) and the real AgentCore Runtime (production) without code changes.

### Requirement 14: Remote MCP Tool Integration

**User Story:** As a .NET developer building agents with the Microsoft Agent Framework, I want to connect remote MCP servers and use their tools in my agent, so that I can extend my agent's capabilities without writing custom tool code.

#### Acceptance Criteria

1. THE RemoteMcpAgent sample SHALL demonstrate connecting to a remote MCP server via `HttpClientTransport`.
2. THE RemoteMcpAgent sample SHALL fetch tools from the MCP server using `McpClient.ListToolsAsync()`.
3. THE RemoteMcpAgent sample SHALL pass MCP tools to the Microsoft Agent Framework via `ChatClientAgentRunOptions.ChatOptions.Tools`.
4. THE MCP connection SHALL be lazy-initialized on first invocation and cached for subsequent requests.

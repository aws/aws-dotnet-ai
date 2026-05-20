# Implementation Plan: Aspire Local Development Experience

## Overview

This plan implements the .NET Aspire-based local development experience for AWS.AgentCore. The solution uses a single self-contained NuGet package (`AWS.AgentCore.Testing`) that embeds all emulator logic (Runtime Emulator, Memory Emulator) and a ChatBot UI (Blazor Server) directly within the package. All emulators run as in-process Kestrel servers within the AppHost. `AddAgentCoreRuntime<TProject>()` returns `IResourceBuilder<ProjectResource>` for deployment compatibility, and a custom `WithReference` overload injects `AGENTCORE_SERVICE_ENDPOINT` for consuming apps.

## Tasks

- [x] 1. Create the AWS.AgentCore.Testing package with embedded emulators
  - [x] 1.1 Create the Testing package project structure (Microsoft.NET.Sdk.Web, NuGet metadata)
  - [x] 1.2 Implement PortAllocator utility
  - [x] 1.3 Implement AgentCoreTestingExtensions with AddAgentCoreRuntime<TProject>
  - [x] 1.4 Implement WithStreaming() and WithInMemory() as extension methods on IResourceBuilder<ProjectResource>
  - [x] 1.5 Implement WithReference() overload for injecting AGENTCORE_SERVICE_ENDPOINT
  - [x] 1.6 Implement AgentCoreRuntimeAnnotation (internal, stores runtimePort/chatAppPort/isStreaming)

- [x] 2. Implement embedded Runtime Emulator
  - [x] 2.1 Implement RuntimeEmulatorServer with explicit route `POST /runtimes/{agentRuntimeArn}/invocations`
  - [x] 2.2 Implement RuntimeEmulatorService with raw payload passthrough (no JSON wrapping)
  - [x] 2.3 Implement ping/readiness check with exponential backoff
  - [x] 2.4 Support SSE stream passthrough
  - [x] 2.5 Accept optional ILoggerProvider for Aspire Dashboard log integration

- [x] 3. Implement embedded Memory Emulator
  - [x] 3.1 Implement MemoryEmulatorServer (CreateEvent + ListEvents endpoints)
  - [x] 3.2 Implement InMemoryEventStore (thread-safe, paginated, chronological)
  - [x] 3.3 Create Memory Emulator models

- [x] 4. Implement embedded ChatBot UI
  - [x] 4.1 Implement ChatAppServer (Blazor Server, static file serving for NuGet)
  - [x] 4.2 Create Blazor components (Home, Layout, Sidebar)
  - [x] 4.3 Implement configurable payload editor with {{paramName}} placeholders
  - [x] 4.4 Implement dynamic parameters (string, number, boolean, raw JSON types)
  - [x] 4.5 Implement code editor component (line numbers, auto-indent, bracket matching)
  - [x] 4.6 Implement built-in payload templates
  - [x] 4.7 Implement light/dark theme with cookie-based persistence
  - [x] 4.8 Add AWS logo branding (theme-aware svg assets)
  - [x] 4.9 Implement live payload preview
  - [x] 4.10 Implement session management and streaming support

- [x] 5. NuGet packaging and static asset distribution
  - [x] 5.1 Implement build targets to assemble wwwroot (scoped CSS, blazor.web.js, collocated JS)
  - [x] 5.2 Create build/AWS.AgentCore.Testing.targets for consumers
  - [x] 5.3 Implement GetWwwrootCopyItems target for project reference propagation
  - [x] 5.4 Implement IncludeWwwrootInPackage target for NuGet content

- [x] 6. Implement AspireLoggerProvider
  - [x] 6.1 Create AspireLoggerProvider that bridges ILoggerProvider to ResourceLoggerService
  - [x] 6.2 Wire runtime emulator logs to Aspire Dashboard

- [x] 7. Create sample AppHost
  - [x] 7.1 Create sampleapps/AspireAppHost with multiple agents
  - [x] 7.2 Demonstrate WithReference(agent) with ChatBotUI sample

- [x] 8. Create ChatBotUI sample app
  - [x] 8.1 Implement Program.cs reading AGENTCORE_SERVICE_ENDPOINT
  - [x] 8.2 Override AWS SDK ServiceURL when env var is present
  - [x] 8.3 Default RuntimeArn to "local-agent" when using emulator
  - [x] 8.4 Fall back to standard AWS SDK with Region/RuntimeArn from appsettings.json

- [x] 9. Create RemoteMcpAgent sample app
  - [x] 9.1 Implement McpToolProvider with lazy async connection
  - [x] 9.2 Connect to remote MCP server via HttpClientTransport
  - [x] 9.3 Pass MCP tools via ChatClientAgentRunOptions at invocation time
  - [x] 9.4 Configure with DeepWiki public MCP server

- [x] 10. Write unit tests
  - [x] 10.1 InMemoryEventStore tests (CRUD, filtering, pagination, ordering)
  - [x] 10.2 RuntimeEmulatorService tests (headers, raw passthrough, session management)
  - [x] 10.3 AgentCoreTestingExtensions tests (env vars, ports, annotation, resource registration)
  - [x] 10.4 Property-based tests (FsCheck) for store and emulator properties

- [x] 11. Write integration tests
  - [x] 11.1 Reference AspireAppHost via DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireAppHost>()
  - [x] 11.2 Test: Agent ping endpoint returns healthy
  - [x] 11.3 Test: Agent invocations endpoint returns response with message field

- [x] 12. Fix CI test issues
  - [x] 12.1 Fix localhost:0 binding error (use 127.0.0.1:0 for .NET 10)
  - [x] 12.2 Fix RuntimeEmulatorService test for raw payload passthrough
  - [x] 12.3 Fix integration test project to reference separate AppHost (not self-host)
  - [x] 12.4 Fix property test resource exhaustion (single builder, many agents)

## Notes

- `AddAgentCoreRuntime<TProject>()` returns `IResourceBuilder<ProjectResource>` — no custom resource type
- `AgentCoreRuntimeAnnotation` (internal) stores runtimePort, chatAppPort, isStreaming on the project resource
- Custom `WithReference` overload injects `AGENTCORE_SERVICE_ENDPOINT` env var (not a connection string)
- Runtime emulator passes payload through as-is — no `{"prompt":"..."}` wrapping
- Runtime emulator uses explicit route `/runtimes/{agentRuntimeArn}/invocations`
- ChatBot UI uses the AWS SDK to talk to the runtime emulator (same path as production)
- NuGet package includes `.targets` file that copies wwwroot to consumer's output
- Integration tests use `DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireAppHost>()` (separate project)
- ChatBot UI static assets are served via PhysicalFileProvider in Production environment mode
- `AspireLoggerProvider` pipes embedded server logs to Aspire Dashboard
- Theme preference persisted via cookie (shared across ports on localhost)

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["1.3", "1.4", "1.5", "1.6"] },
    { "id": 2, "tasks": ["2.1", "2.2", "2.3", "2.4", "2.5", "3.1", "3.2", "3.3"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3", "4.4", "4.5", "4.6", "4.7", "4.8", "4.9", "4.10"] },
    { "id": 4, "tasks": ["5.1", "5.2", "5.3", "5.4"] },
    { "id": 5, "tasks": ["6.1", "6.2", "7.1", "7.2"] },
    { "id": 6, "tasks": ["8.1", "8.2", "8.3", "8.4", "9.1", "9.2", "9.3", "9.4"] },
    { "id": 7, "tasks": ["10.1", "10.2", "10.3", "10.4"] },
    { "id": 8, "tasks": ["11.1", "11.2", "11.3"] },
    { "id": 9, "tasks": ["12.1", "12.2", "12.3", "12.4"] }
  ]
}
```

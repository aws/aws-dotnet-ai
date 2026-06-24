## Release 2026-06-24

### AWS.AgentCore.Hosting (0.1.0-preview)
* Added OpenTelemetry instrumentation support. IChatClient and AIAgent are wrapped with .UseOpenTelemetry() decorators that emit traces and metrics under standard Microsoft AI activity sources. Users wire their own OTel pipeline and call AddAgentCoreInstrumentation() on TracerProviderBuilder/MeterProviderBuilder to subscribe AgentCore sources and meters.
* Fixed request deserialization to not require Content-Type: application/json. The AgentCore Runtime forwards requests without a JSON content type; the /invocations endpoint now reads the body directly via JsonSerializer instead of ReadFromJsonAsync.

## Release 2026-06-05

### AWS.AgentCore.Hosting (0.0.1-preview)
* AgentCore Runtime endpoint mapping (POST /invocations, GET /ping) with Minimal API-style parameter binding
* Source generator for zero-boilerplate agent development ([AgentCoreStartup], [AgentCoreHandler], [AgentCorePing])
* SSE streaming support via IAsyncEnumerable<string>
* Microsoft Agent Framework integration (IChatClient, ChatClientAgent, agent middleware pipeline)
* AgentCore Memory integration for session-scoped conversation history
* NativeAOT support with JsonSerializerContext overloads
### AWS.AgentCore.Testing (0.0.1-preview)
* Runtime Emulator server for local AgentCore SDK request handling
* Memory Emulator server with in-memory conversation event storage
* Chat App web UI with payload editor, session management, and markdown rendering
* Payload configuration persistence across restarts

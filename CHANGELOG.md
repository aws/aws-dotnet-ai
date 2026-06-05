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

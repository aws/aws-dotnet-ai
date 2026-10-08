## Release 2026-10-08

### AWS.Bedrock.MEAI (1.1.0)
* Fixed BedrockChatClient dropping or truncating redacted reasoning content and sending an invalid ReasoningContentBlock union on replay (#76)
* Fix issue with ConverseStreamResponse not honoring cancellation and handling a disposable object
* Added opt-in coalescing of consecutive request messages that map to the same Converse role, configured via the new BedrockChatClientOptions passed to AsIChatClient / BedrockChatClient (#77)
* Surface the native Bedrock stop reason and guardrail trace on ChatResponse.AdditionalProperties (and streaming updates) via AmazonBedrockRuntimeExtensions.StopReasonKey/TraceKey

## Release 2026-10-06

### AWS.Bedrock.MEAI (1.0.1)
* Set ToolResultBlock.Status to error when FunctionResultContent.Exception is present
* Fixed BedrockChatClient embedding raw streamed tool input in the parse-error Exception.Message attached to FunctionCallContent; it now uses a generic message and preserves the parser exception as the inner exception (#80)
* Fix #73. Preserve integer precision when converting JSON numbers in tool results and tool-call arguments to Bedrock documents. Integers that fit in Int32 or Int64 are no longer rounded through double.

## Release 2026-09-23

### AWS.AgentCore.Hosting (1.1.2)
* Ensure AgentCore memory events saved within a session receive strictly-increasing timestamps so conversation history replays in chronological order.

## Release 2026-08-20 #2

### AWS.AgentCore.Hosting (1.1.1)
* Fixed AgentCoreMemoryProvider replaying conversation history in reverse (newest-first) order. The AgentCore Memory ListEvents API returns events newest-first; the provider now sorts them ascending by EventTimestamp so chat history is presented oldest-first and multi-turn follow-ups bind to the most recent turn.
### AWS.AgentCore.Testing (1.0.1)
* Updated the in-memory Memory emulator (InMemoryEventStore.ListEvents) to return events newest-first, matching the ordering of the real Amazon Bedrock AgentCore Memory ListEvents API so local tests exercise the same ordering behavior as production.

## Release 2026-08-20

### AWS.AgentCore.Hosting (1.1.0)
* Updated the Bedrock Microsoft.Extensions.AI dependency to the in-repo AWS.Bedrock.MEAI package, replacing the older AWSSDK.Extensions.Bedrock.MEAI package. The public API (Amazon.BedrockRuntime namespace and AmazonBedrockRuntimeExtensions methods) is unchanged.

## Release 2026-08-06

### AWS.Bedrock.MEAI (1.0.0)
* Migrated the Bedrock Microsoft.Extensions.AI integration into the aws-dotnet-ai repository. This package was previously published as AWSSDK.Extensions.Bedrock.MEAI from the aws-sdk-net repository. The AmazonBedrockRuntimeExtensions methods remain in the Amazon.BedrockRuntime namespace; the package's other types now live in the AWS.Bedrock.MEAI namespace.

## Release 2026-08-04

### AWS.AgentCore.Hosting (1.0.0)
* Promoted AWS.AgentCore.Hosting to its first stable 1.0.0 release.
### AWS.AgentCore.Testing (1.0.0)
* Promoted AWS.AgentCore.Testing to its first stable 1.0.0 release.

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

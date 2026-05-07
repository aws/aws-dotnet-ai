# Implementation Plan: Microsoft Agent Framework Integration

## Overview

This plan implements deep integration of Microsoft Agent Framework 1.0 into AWS.AgentCore. The work modifies `AgentCoreOptions` to support optional Bedrock and explicit `IChatClient` providers, refactors `AddAgentCore()` to register `ChatClientAgent` and `AgentCoreRuntimeContextProvider` in DI, creates new helper classes, updates sample apps, and adds comprehensive unit tests. All code is C#/.NET 10.

## Tasks

- [x] 1. Modify AgentCoreOptions to support new properties
  - [x] 1.1 Update AgentCoreOptions class with new properties
    - Change `ModelId` from `string` (defaulting to `string.Empty`) to `string?` (nullable, defaulting to `null`)
    - Add `ChatClient` property of type `IChatClient?` (default `null`)
    - Add `AgentOptions` property of type `ChatClientAgentOptions?` (default `null`)
    - Add `ConfigureAgent` property of type `Func<ChatClientAgent, ChatClientAgent>?` (default `null`)
    - Add XML doc comments for each new property explaining its purpose and priority
    - Add required `using` statements for `Microsoft.Extensions.AI` and `Microsoft.Agents.AI`
    - _Requirements: 2.1, 2.2, 2.3, 5.1, 5.4, 10.2_

- [x] 2. Refactor AddAgentCore() extension method
  - [x] 2.1 Implement IChatClient priority resolution logic
    - Remove the `ArgumentException` throw when `ModelId` is empty/null
    - Implement three-path IChatClient registration: (1) `options.ChatClient` set → register as singleton, (2) `options.ModelId` set → register Bedrock IChatClient, (3) neither → skip registration (rely on pre-registered DI)
    - Ensure `ChatClient` property takes precedence over `ModelId` when both are set
    - Keep `AddAWSService<IAmazonBedrockRuntime>()` only in the ModelId path
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 7.1, 7.3_

  - [x] 2.2 Register ChatClientAgent in DI
    - Add singleton registration for `ChatClientAgent` using a factory delegate
    - In the factory: resolve `IChatClient` from DI, throw `InvalidOperationException` with descriptive message if null
    - Use `options.AgentOptions ?? new ChatClientAgentOptions()` for agent construction
    - Apply `options.ConfigureAgent` callback if provided
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 5.1, 5.4_

  - [x] 2.3 Register AgentCoreRuntimeContextProvider in DI
    - Add singleton registration for `AgentCoreRuntimeContextProvider`
    - Ensure it is registered unconditionally (always present when AddAgentCore is called)
    - _Requirements: 4.1, 4.3, 4.4_

- [x] 3. Create AgentCoreRuntimeContextProvider class
  - [x] 3.1 Implement AgentCoreRuntimeContextProvider
    - Create new file `src/AWS.AgentCore/AgentCoreRuntimeContextProvider.cs`
    - Inherit from `AIContextProvider` (from `Microsoft.Agents.AI`)
    - Define `public const string ContextKey = "AgentCore.RuntimeContext"`
    - Override `InvokingAsync` method — return empty `ChatMessage` enumerable (context is stored in session properties by the caller)
    - Add XML doc comments explaining the class purpose and usage
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 9.2_

- [x] 4. Create AgentCoreSessionFactory internal helper
  - [x] 4.1 Implement AgentCoreSessionFactory
    - Create new file `src/AWS.AgentCore/Internal/AgentCoreSessionFactory.cs`
    - Create `internal static class AgentCoreSessionFactory`
    - Implement `CreateSessionAsync` static method that takes `ChatClientAgent`, `AgentCoreRuntimeContext?`, and `CancellationToken`
    - Call `agent.CreateSessionAsync()` to get a session
    - Store runtime context in session properties using `AgentCoreRuntimeContextProvider.ContextKey` if context is not null
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

- [x] 5. Checkpoint - Verify core library compiles
  - Ensure the solution builds without errors after the core changes. Ask the user if questions arise.

- [x] 6. Update MicrosoftAgentFrameworkSample to use ChatClientAgent from DI
  - [x] 6.1 Refactor MicrosoftAgentFrameworkSample/Program.cs
    - Update the `MapAgentCore` handler to resolve `ChatClientAgent` from DI instead of calling `chatClient.AsAIAgent()` inline
    - Use `agent.CreateSessionAsync()` and `agent.RunAsync()` pattern
    - Add `ChatClientAgentOptions` with tools configured via `options.AgentOptions` in the `AddAgentCore` callback
    - Ensure the sample demonstrates the new recommended pattern
    - _Requirements: 1.1, 1.2, 5.1, 5.5_

- [x] 7. Update StreamingAgent sample to use ChatClientAgent from DI
  - [x] 7.1 Refactor StreamingAgent/Program.cs
    - Update the handler to resolve `ChatClientAgent` from DI
    - Use `agent.RunStreamingAsync()` with the DI-resolved agent
    - Configure tools via `options.AgentOptions` in the `AddAgentCore` callback
    - Demonstrate streaming with the full MS AF pipeline
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 8. Update AnnotationsSample to use ChatClientAgent from DI
  - [x] 8.1 Refactor AnnotationsSample Agent.cs and Startup.cs
    - Update `Startup.ConfigureServices` to configure `AgentOptions` with tools
    - Update `Agent` class constructor to inject `ChatClientAgent` instead of `IChatClient`
    - Update `HandleInvocation` to use the injected `ChatClientAgent` directly
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 9. Checkpoint - Verify samples compile and patterns are correct
  - Ensure all sample apps compile without errors. Ask the user if questions arise.

- [x] 10. Write unit tests for DI registration logic
  - [x] 10.1 Create AddAgentCore DI registration tests
    - Create new test file `test/AWS.AgentCore.UnitTests/AddAgentCoreRegistrationTests.cs`
    - Test: `AddAgentCore_WithModelId_RegistersBedrockIChatClient` — verify IChatClient is registered when ModelId is set
    - Test: `AddAgentCore_WithChatClient_RegistersExplicitClient` — verify explicit ChatClient is used
    - Test: `AddAgentCore_WithBothChatClientAndModelId_ChatClientWins` — verify priority
    - Test: `AddAgentCore_WithPreRegisteredIChatClient_DoesNotOverwrite` — verify pre-registered DI is preserved
    - Test: `AddAgentCore_WithNoIChatClient_ThrowsOnResolution` — verify descriptive InvalidOperationException
    - Test: `AddAgentCore_WithAgentOptions_PassesToChatClientAgent` — verify options forwarding
    - Test: `AddAgentCore_WithConfigureAgent_AppliesCallback` — verify middleware decoration
    - Test: `AddAgentCore_WithModelIdOnly_BackwardCompatible` — verify existing code still works
    - Test: `AddAgentCore_RegistersChatClientAgent` — verify ChatClientAgent is in DI
    - Test: `AddAgentCore_RegistersAgentCoreRuntimeContextProvider` — verify context provider is in DI
    - Test: `AddAgentCore_WithNoConfig_DoesNotThrowAtRegistration` — verify lazy failure
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 7.1, 7.3_

  - [x] 10.2 Write unit tests for AgentCoreRuntimeContextProvider
    - Test that the provider returns empty messages from InvokingAsync
    - Test that the ContextKey constant is correctly defined
    - _Requirements: 4.1, 4.2_

  - [x] 10.3 Write unit tests for AgentCoreSessionFactory
    - Test that CreateSessionAsync stores runtime context in session properties
    - Test that null runtime context does not throw
    - _Requirements: 3.1, 3.2, 3.3_

- [x] 11. Verify NativeAOT compatibility
  - [x] 11.1 Verify NativeAotAnnotations sample compiles with PublishAot=true
    - Build the `NativeAotAnnotations` sample with `dotnet publish -c Release` (it already has PublishAot=true)
    - Verify no new trimming warnings related to MS AF registration (`ChatClientAgent`, `AgentCoreRuntimeContextProvider`)
    - If warnings appear, add appropriate `[DynamicDependency]` or `[UnconditionalSuppressMessage]` attributes
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 12. Verify source generator still works with new DI registrations
  - [x] 12.1 Verify source generator output is compatible
    - Build the `AnnotationsSample` project and verify the generated `AgentCore_GeneratedProgram.g.cs` still compiles
    - Verify that `ChatClientAgent` can be resolved from DI in the generated code (it uses `services.GetRequiredService<Agent>()` which injects via constructor)
    - Run existing source generator snapshot tests to confirm no regressions
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 13. Final checkpoint - Ensure all tests pass
  - Run `dotnet test` across the solution. Ensure all unit tests pass and no build warnings are introduced. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- The design explicitly states property-based testing does not apply to this feature (DI wiring, configuration-driven behavior)
- The `ConfigureAgent` callback uses `Func<ChatClientAgent, ChatClientAgent>` — the design shows `AsBuilder().Use().Build()` returning a new agent instance
- Backward compatibility is critical: existing code with only `ModelId` set must continue to work identically
- The source generator does NOT need code changes — it already resolves services from DI via constructor injection

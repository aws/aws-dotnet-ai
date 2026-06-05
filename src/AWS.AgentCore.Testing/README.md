# AWS.AgentCore.Testing

[![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.Testing.svg) ![downloads](https://img.shields.io/nuget/dt/AWS.AgentCore.Testing.svg)](https://www.nuget.org/packages/AWS.AgentCore.Testing/)

A local development and testing package for [Amazon Bedrock AgentCore](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/) agents. Provides embedded emulator servers that let you test your agent without deploying to AWS — no AWS account or credentials required.

## What's Included

- **Runtime Emulator** — An in-process server that emulates the AgentCore Runtime API, accepting AWS SDK requests locally and forwarding them to your agent's `/invocations` endpoint
- **Memory Emulator** — An in-memory implementation of the AgentCore Memory API for testing conversation history persistence
- **Chat App** — A web-based UI for interactively testing your agent with a configurable payload editor, session management, and markdown rendering

## Getting Started

Install the package:

```
dotnet add package AWS.AgentCore.Testing
```

Start the emulators and point them at your running agent:

```csharp
using AWS.AgentCore.Testing;

// Your agent is running on http://localhost:8080 (the AgentCore default port)

// Start the runtime emulator — it accepts AWS SDK requests and forwards to your agent
var runtimeApp = RuntimeEmulatorServer.Create("http://localhost:8080", port: 5100);
await runtimeApp.StartAsync();

// Start the chat app — a web UI for interacting with your agent
var chatApp = ChatAppServer.Create("http://localhost:5100", port: 5200);
await chatApp.StartAsync();

// Start the memory emulator — provides in-memory conversation storage
var memoryApp = MemoryEmulatorServer.Create(port: 5300);
await memoryApp.StartAsync();

// Open http://localhost:5200 in your browser to chat with your agent
Console.WriteLine("Chat App: http://localhost:5200");
Console.WriteLine("Runtime Emulator: http://localhost:5100");
Console.ReadLine();
```

## Using the Runtime Emulator with the AWS SDK

Point your `AmazonBedrockAgentCoreClient` at the Runtime Emulator instead of the real AWS endpoint:

```csharp
using Amazon.BedrockAgentCore;
using Amazon.Runtime;

var client = new AmazonBedrockAgentCoreClient(
    new AnonymousAWSCredentials(),
    new AmazonBedrockAgentCoreConfig
    {
        ServiceURL = "http://localhost:5100"
    });

var response = await client.InvokeAgentRuntimeAsync(new InvokeAgentRuntimeRequest
{
    AgentRuntimeArn = "local-agent",
    Payload = "{\"prompt\": \"Hello!\"}"
});
```

No real AWS credentials are needed — the emulator accepts anonymous credentials.

## Chat App Features

The Chat App at `http://localhost:5200` provides:

- **Payload Editor** — Configure the JSON shape your agent expects. Use `{{input}}` as a placeholder for the chat message.
- **Session Management** — Create, switch, and delete chat sessions. Session IDs are passed to the Runtime Emulator.
- **Streaming** — Enable SSE streaming mode by passing `streaming: true` to `ChatAppServer.Create()`.
- **Payload Persistence** — Custom configurations are saved to `~/.agentcore/testing/{agentName}/` and restored across restarts.
- **Dark/Light Mode** — Toggle theme from the header.
- **Documentation** — Built-in docs page accessible from the header.

## Memory Emulator

The Memory Emulator provides an in-memory implementation of the AgentCore Memory APIs (ListEvents, CreateEvent). Set the `AWS_AGENTCORE_SERVICE_ENDPOINT` environment variable on your agent to point at the Memory Emulator:

```
AWS_AGENTCORE_SERVICE_ENDPOINT=http://localhost:5300
AWS_AGENTCORE_MEMORY_ID=localdev-memory
```

Your agent's `AgentCoreMemoryProvider` will automatically use the emulator for loading and saving conversation history.

## Features

- **No Docker required** — All servers run as embedded in-process Kestrel instances
- **No AWS account needed** — Anonymous credentials work with the emulators
- **Port 0 support** — Pass `port: 0` for OS-assigned ports; read the actual port from `app.Urls` after startup
- **Logger injection** — Pass an `ILoggerProvider` to redirect emulator logs to your preferred sink

## License

This project is licensed under the Apache-2.0 License.

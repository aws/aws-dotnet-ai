# Amazon Bedrock AgentCore for .NET

[![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.svg)](https://www.nuget.org/packages/AWS.AgentCore/)

**Amazon Bedrock AgentCore for .NET** is a .NET library for building AI agents that deploy to [Amazon Bedrock AgentCore](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/). It provides the runtime integration layer between your .NET agent code and the AgentCore service — handling the HTTP contract, streaming, session management, memory, and observability — so you can focus on your agent's logic.

Built on top of [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/), Amazon Bedrock AgentCore for .NET gives you access to the full .NET AI ecosystem (tool calling, middleware, multi-agent workflows, MCP) while providing a zero-friction path to production on AWS.

## Key Features

- **Two developer experiences**: Source generator annotations (`[AgentCoreHandler]`) for zero-boilerplate, or extension methods (`app.MapAgentCore<T>()`) for explicit control
- **Streaming support**: SSE via `IAsyncEnumerable<string>` with proper AgentCore wire format
- **Microsoft Agent Framework integration**: `IChatClient`, `ChatClientAgent`, tool calling, agent middleware pipeline
- **AgentCore Memory**: Session-scoped short-term conversation history via `AgentCoreMemoryProvider`
- **NativeAOT support**: Source-generated JSON, `JsonTypeInfo<T>` overloads — deploy as a single self-contained binary
- **OpenTelemetry**: Auto-configured tracing, metrics, and structured logging
- **Local development with Aspire**: Runtime emulator, memory emulator, and chat UI — test your agent without deploying to AWS
- **Model flexibility**: Bedrock models by default, or bring your own `IChatClient` (OpenAI, Anthropic, Ollama, etc.)

## Getting Started

Add the NuGet package to your project:

```
dotnet add package AWS.AgentCore
```

### Option 1: Source Generator

Define a startup class and a handler — the source generator produces the `Program.cs` for you:

```csharp
[AgentCoreStartup]
public class Startup
{
    public void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.AddAgentCore(options =>
        {
            options.ModelId = "global.anthropic.claude-opus-4-7";
            options.AgentOptions = new ChatClientAgentOptions
            {
                ChatOptions = new()
                {
                    Tools = [AIFunctionFactory.Create(GetWeather)]
                }
            };
        });
    }
}

public class Agent(ChatClientAgent chatAgent, ILogger<Agent> logger)
{
    [AgentCoreHandler]
    public async Task<string> HandleInvocation(
        PromptRequest request,
        AgentCoreRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var session = await chatAgent.CreateSessionAsync(cancellationToken: cancellationToken);
        var response = await chatAgent.RunAsync(
            request.Prompt ?? "Hello!", session: session, cancellationToken: cancellationToken);
        return response.ToString();
    }
}
```

### Option 2: Extension Methods

Use familiar ASP.NET Core Minimal API patterns:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";
    options.AgentOptions = new ChatClientAgentOptions
    {
        ChatOptions = new()
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
        }
    };
});

var app = builder.Build();

app.MapAgentCore<PromptRequest>(async (
    PromptRequest request,
    ChatClientAgent agent,
    AgentCoreRuntimeContext context,
    CancellationToken cancellationToken) =>
{
    var session = await agent.CreateSessionAsync(cancellationToken: cancellationToken);
    var response = await agent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: cancellationToken);
    return response.ToString();
});

app.Run();
```

### Streaming

Return `IAsyncEnumerable<string>` for SSE streaming responses:

```csharp
app.MapAgentCore<PromptRequest>((PromptRequest request, ChatClientAgent agent, CancellationToken ct) =>
{
    return StreamResponse(ct);

    async IAsyncEnumerable<string> StreamResponse([EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        await foreach (var update in agent.RunStreamingAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
});
```

## AgentCore Memory

Enable persistent conversation history with a single environment variable or option:

```csharp
builder.AddAgentCore(options =>
{
    options.MemoryId = "my-memory-id"; // or set AWS_AGENTCORE_MEMORY_ID env var
    options.ModelId = "global.anthropic.claude-opus-4-7";
});
```

The `AgentCoreMemoryProvider` automatically loads and saves conversation history per session using AgentCore's Memory APIs. No additional code required.

## Local Development with Aspire

Test your agent locally with a full emulation stack — no AWS account needed:

```csharp
// In your Aspire AppHost
var agent = builder.AddAgentCoreRuntime<Projects.MyAgent>()
    .WithStreaming()
    .WithInMemory();
```

This starts:

- **Runtime Emulator** — accepts AWS SDK requests locally
- **Memory Emulator** — in-memory conversation storage
- **Chat App** — web UI for interactive testing

Install the [`AWS.AgentCore.Testing`](https://www.nuget.org/packages/AWS.AgentCore.Testing/) package for the emulator servers. Aspire extensions are available in [`Aspire.Hosting.AWS`](https://www.nuget.org/packages/Aspire.Hosting.AWS/).

## Agent Middleware

Decorate your agent with middleware using the Microsoft Agent Framework pipeline:

```csharp
builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";

    options.ConfigureAgent = agent => agent
        .AsBuilder()
        .Use(async (context, request, next, ct) =>
        {
            Console.WriteLine($"Before: {request}");
            var response = await next(context, request, ct);
            Console.WriteLine($"After: {response}");
            return response;
        })
        .Build();
});
```

## OpenTelemetry

Observability is configured automatically by `AddAgentCore()`. Traces, metrics, and structured logs export to:

- **Aspire Dashboard** when running locally (auto-detected via `OTEL_EXPORTER_OTLP_ENDPOINT`)
- **AgentCore OTEL sidecar** (`localhost:4318`) in production (Not yet available)

Custom spans are emitted for each agent invocation with session and request metadata. Disable with `options.DisableObservability = true`.

## NativeAOT

For minimal cold-start times, deploy as a NativeAOT binary. Use the `JsonTypeInfo<T>` overload:

```csharp
app.MapAgentCore<PromptRequest>(
    async (request, context, services, ct) =>
    {
        var agent = services.GetRequiredService<ChatClientAgent>();
        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: ct);
        return response.ToString();
    },
    AppJsonContext.Default.PromptRequest);
```

## Sample Applications

| Sample                                                                      | Description                                  |
| --------------------------------------------------------------------------- | -------------------------------------------- |
| [AnnotationsSample](./sampleapps/AnnotationsSample)                         | Source generator with DI, tools, custom ping |
| [StreamingAgent](./sampleapps/StreamingAgent)                               | SSE streaming with extension methods         |
| [MicrosoftAgentFrameworkSample](./sampleapps/MicrosoftAgentFrameworkSample) | Agent + function middleware, multiple tools  |
| [NativeAotAnnotations](./sampleapps/NativeAotAnnotations)                   | NativeAOT with source generator              |
| [NativeAotExtensions](./sampleapps/NativeAotExtensions)                     | NativeAOT with extension methods             |
| [RemoteMcpAgent](./sampleapps/RemoteMcpAgent)                               | MCP tool provider integration                |
| [AspireAppHost](./sampleapps/AspireAppHost)                                 | Local dev experience with Aspire             |

## Getting Help

For feature requests or issues using this library please open an [issue in this repository](https://github.com/aws/aws-dotnet-ai/issues).

## Contributing

We welcome community contributions and pull requests. See [CONTRIBUTING.md](./CONTRIBUTING.md) for information on how to submit code.

## Security

See [CONTRIBUTING](CONTRIBUTING.md#security-issue-notifications) for more information.

## License

This project is licensed under the Apache-2.0 License.

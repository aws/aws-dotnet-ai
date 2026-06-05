# AWS.AgentCore.Hosting

[![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.Hosting.svg) ![downloads](https://img.shields.io/nuget/dt/AWS.AgentCore.Hosting.svg)](https://www.nuget.org/packages/AWS.AgentCore.Hosting/)

A .NET library for building AI agents that deploy to [Amazon Bedrock AgentCore](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/). It provides the runtime integration layer between your .NET agent code and the AgentCore service — handling the HTTP contract, streaming, session management, memory, and observability — so you can focus on your agent's logic.

Built on top of [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/), this package gives you access to the full .NET AI ecosystem (tool calling, middleware, multi-agent workflows, MCP) while providing a zero-friction path to production on AWS.

## Key Features

- **Two developer experiences**: Source generator annotations (`[AgentCoreHandler]`) for zero-boilerplate, or extension methods (`app.MapAgentCore<T>()`) for explicit control
- **Streaming support**: SSE via `IAsyncEnumerable<string>` with proper AgentCore wire format
- **Microsoft Agent Framework integration**: `IChatClient`, `ChatClientAgent`, tool calling, agent middleware pipeline
- **AgentCore Memory**: Session-scoped short-term conversation history via `AgentCoreMemoryProvider`
- **NativeAOT support**: Source-generated JSON, `JsonSerializerContext` overloads — deploy as a single self-contained binary
- **Model flexibility**: Bedrock models by default, or bring your own `IChatClient` (OpenAI, Anthropic, Ollama, etc.)

## Getting Started

Create a new ASP.NET Core web project:

```
dotnet new web -n MyAgent
cd MyAgent
```

Add the NuGet package:

```
dotnet add package AWS.AgentCore.Hosting
```

### Option 1: Source Generator

Define a startup class and a handler — the source generator produces the `Program.cs` for you:

```csharp
using AWS.AgentCore.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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

    [Description("Gets the current weather for a given location.")]
    public static string GetWeather([Description("The city or location.")] string location)
        => $"The weather in {location} is 72°F and sunny.";
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

public record PromptRequest(string? Prompt);
```

### Option 2: Extension Methods

Use familiar ASP.NET Core Minimal API patterns:

```csharp
using AWS.AgentCore.Hosting;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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

[Description("Gets the current weather for a given location.")]
static string GetWeather([Description("The city or location.")] string location)
    => $"The weather in {location} is 72°F and sunny.";

public record PromptRequest(string? Prompt);
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

## NativeAOT

For minimal cold-start times, deploy as a NativeAOT binary. Use the `JsonSerializerContext` overload:

```csharp
app.MapAgentCore<PromptRequest>(
    async (request, context, services, ct) =>
    {
        var agent = services.GetRequiredService<ChatClientAgent>();
        var session = await agent.CreateSessionAsync(cancellationToken: ct);
        var response = await agent.RunAsync(request.Prompt ?? "Hello!", session: session, cancellationToken: ct);
        return response.ToString();
    },
    AppJsonContext.Default);
```

## License

This project is licensed under the Apache-2.0 License.

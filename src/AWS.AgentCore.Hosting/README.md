# AWS.AgentCore.Hosting

[![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.Hosting.svg) ![downloads](https://img.shields.io/nuget/dt/AWS.AgentCore.Hosting.svg)](https://www.nuget.org/packages/AWS.AgentCore.Hosting/)

A .NET library for building AI agents that deploy to [Amazon Bedrock AgentCore](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/). It provides the runtime integration layer between your .NET agent code and the AgentCore service — handling the HTTP contract, streaming, session management, memory, and observability — so you can focus on your agent's logic.

Built on top of [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/), this package gives you access to the full .NET AI ecosystem (tool calling, middleware, multi-agent workflows, MCP) while providing a zero-friction path to production on AWS.

## Key Features

- **Two developer experiences**: Source generator annotations (`[AgentCoreHandler]`) for zero-boilerplate, or extension methods (`app.MapAgentCore<T>()`) for explicit control
- **Streaming support**: SSE via `IAsyncEnumerable<string>` with proper AgentCore wire format
- **Microsoft Agent Framework integration**: `IChatClient`, `ChatClientAgent`, tool calling, agent middleware pipeline
- **AgentCore Memory**: Session-scoped short-term conversation history via `AgentCoreMemoryProvider`
- **OpenTelemetry observability**: Traces, metrics, and logs out of the box — wire-compatible with AgentCore Runtime's OTLP sidecar, the Aspire Dashboard, and any OTLP-compatible backend
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

## Observability

OpenTelemetry instrumentation is built into the package and works in two modes — pick the one that matches how your application configures OpenTelemetry:

**Mode 1 — Standalone agent (no Aspire / custom OTel):** flip the `EnableObservability` toggle and `AddAgentCore()` registers a turnkey OTLP pipeline targeting the AgentCore Runtime sidecar (`http://localhost:4318`, HTTP/Protobuf) with ASP.NET Core, HttpClient, and AWS SDK instrumentation, plus OTLP exporters for traces, metrics, and logs.

```csharp
builder.AddAgentCore(options =>
{
    options.ModelId = "...";
    options.EnableObservability = true;   // turnkey OTLP pipeline
});
```

**Mode 2 — Aspire `ServiceDefaults` / custom OTel pipeline:** leave `EnableObservability` off (the default), then call `AddAgentCoreInstrumentation()` on your `TracerProviderBuilder` and `MeterProviderBuilder` to subscribe the AgentCore activity sources, meters, and AWS SDK instrumentation. Same shape as `AddAspNetCoreInstrumentation()` and `AddHttpClientInstrumentation()`.

```csharp
builder.AddServiceDefaults();   // your existing AddOpenTelemetry().AddOtlpExporter() setup
builder.AddAgentCore(options => { options.ModelId = "..."; });

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAgentCoreInstrumentation())
    .WithMetrics(m => m.AddAgentCoreInstrumentation());
```

Either way, `AddAgentCore()` always wraps `IChatClient` and `AIAgent` with `.UseOpenTelemetry()`, so the underlying activity sources fire whether or not anyone subscribes — no double-exporting, no risk of the library stomping on your OTel pipeline.

> **⚠️ Production limitation (as of this release)**
>
> AWS Bedrock AgentCore Runtime does **not currently run an OTLP sidecar collector for non-Python agents**. This means telemetry emitted by your .NET agent will not be ingested into CloudWatch when deployed to AgentCore Runtime today. Support is planned but not yet available.
>
> In the meantime, **you can fully exercise the instrumentation locally** by running the [Aspire Dashboard](#local-development-with-aspire) or any OTLP collector (Jaeger, Prometheus, etc.). Once AgentCore Runtime adds .NET sidecar support, no code changes will be needed — telemetry will flow automatically.

### What gets instrumented

When you wire up an agent through `AddAgentCore()`, the package wraps your `IChatClient` and `AIAgent` with [Microsoft Agent Framework's OpenTelemetry decorators](https://learn.microsoft.com/en-us/agent-framework/agents/observability), so you automatically get:

| Span | Description |
|---|---|
| `invoke_agent <agent-name>` | Agent invocation (top-level) |
| `execute_tool <function-name>` | Tool/function call from the agent |
| `chat <model-name>` | Underlying chat model call |
| `aws.agentcore.hosting.invocation` | Internal-kind span for the agent invocation, tagged with `gen_ai.conversation.id` (when a session is present) |

| Metric | Description |
|---|---|
| `gen_ai.client.operation.duration` (histogram, seconds) | Operation duration. Tagged with `gen_ai.operation.name` (`invoke_agent`, `search_memory`, `upsert_memory`, `chat`, `execute_tool`) and `error.type` when the operation fails. The OpenTelemetry resource carries `gen_ai.provider.name` (set to `aws.bedrock` automatically when `options.ModelId` is configured; override via `OTEL_RESOURCE_ATTRIBUTES=gen_ai.provider.name=<your-provider>` for non-Bedrock chat clients). The histogram count also serves as the operation count — no separate counter is needed. |
| `gen_ai.client.token.usage` (histogram) | Input/output token usage (emitted by the wrapped `IChatClient`). |
| `agent_framework.function.invocation.duration` (histogram) | Function invocation duration (emitted by the wrapped `AIAgent`). |

In addition, ASP.NET Core, HttpClient, and AWS SDK requests are instrumented automatically.

### Default OTLP endpoint

By default, the package exports to `http://localhost:4318` over **HTTP/Protobuf** — this matches the OTLP sidecar contract that AgentCore Runtime uses for Python agents today and will use for .NET agents once support is added. No configuration required.

Until .NET sidecar support lands in AgentCore Runtime, the exporter will emit to `localhost:4318`, encounter no listener in production, and silently buffer/drop telemetry. This is non-fatal — the OTLP exporter is designed to fail gracefully when the collector is unreachable.

### Pointing at a different collector

Set any of the standard OpenTelemetry environment variables and the SDK takes over endpoint resolution. As soon as one is set, your defaults win for all signals:

```bash
# Single endpoint for all signals
export OTEL_EXPORTER_OTLP_ENDPOINT=http://my-collector:4318
export OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf

# Or per-signal endpoints
export OTEL_EXPORTER_OTLP_TRACES_ENDPOINT=http://traces-collector:4318/v1/traces
export OTEL_EXPORTER_OTLP_METRICS_ENDPOINT=http://metrics-collector:4318/v1/metrics
export OTEL_EXPORTER_OTLP_LOGS_ENDPOINT=http://logs-collector:4318/v1/logs

# Headers (auth, etc.)
export OTEL_EXPORTER_OTLP_HEADERS="Authorization=Bearer <token>"
```

See the [OpenTelemetry SDK environment variable reference](https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/) for the full list.

### Local development with Aspire

The easiest way to run your agent end-to-end with full OpenTelemetry visibility is through the [`Aspire.Hosting.AWS`](https://www.nuget.org/packages/Aspire.Hosting.AWS/) package, which adds an `AddAgentCoreRuntime<T>()` extension that wires up an embedded runtime emulator, chat UI, and the Aspire Dashboard with OTEL preconfigured.

See [Local Development with AgentCore in the Aspire.Hosting.AWS README](https://github.com/aws/integrations-on-dotnet-aspire-for-aws/blob/dev/src/Aspire.Hosting.AWS/README.md#local-development-with-agentcore-experimental) for a complete walkthrough.

### Sensitive data

By default, prompts, model responses, function arguments, and function results are **not** included in spans or metrics. Enable them only in development or test environments:

```csharp
builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";
    options.EnableSensitiveTelemetryData = true; // dev/test only
});
```

### Customizing tracer / meter / logger providers

If you need to add custom samplers, processors, exporters, or instrumentation, use the configure callbacks:

```csharp
builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";

    options.ConfigureTracing = tracing =>
    {
        // e.g., add a custom sampler or your own ActivitySource
        tracing.AddSource("MyCompany.MyAgent");
        tracing.SetSampler(new TraceIdRatioBasedSampler(0.1));
    };

    options.ConfigureMetrics = metrics =>
    {
        metrics.AddMeter("MyCompany.MyAgent");
    };

    options.ConfigureLogging = logging =>
    {
        logging.IncludeScopes = true;
    };
});
```

### Resource attributes

The exporter automatically sets the following resource attributes:

- `service.name` and `service.version` — derived from the entry assembly (falls back to `OTEL_SERVICE_NAME` if set)
- `cloud.provider=aws` and `cloud.region` — when `AWS_REGION` or `AWS_DEFAULT_REGION` is set

In production, AgentCore Runtime injects [`OTEL_RESOURCE_ATTRIBUTES`](https://opentelemetry.io/docs/specs/otel/configuration/sdk-environment-variables/#otel_resource_attributes) which contains `cloud.resource_id=<runtime-arn>` and other identifying attributes. The OpenTelemetry SDK merges these into the resource automatically — no extra configuration required.

You can add custom resource attributes via `OTEL_RESOURCE_ATTRIBUTES`:

```bash
export OTEL_RESOURCE_ATTRIBUTES="deployment.environment=staging,team.name=ml-platform"
```

### Disabling observability

To opt out entirely (no OTEL registration, no instrumentation wrapping):

```csharp
builder.AddAgentCore(options =>
{
    options.ModelId = "global.anthropic.claude-opus-4-7";
    options.DisableObservability = true;
});
```

### Resolving `ChatClientAgent` vs `AIAgent`

When observability is enabled, the `AIAgent` registered in DI is wrapped with `OpenTelemetryAgent` for full agent-level instrumentation (`invoke_agent`, `execute_tool`, `agent_framework.function.invocation.duration`). Resolve `AIAgent` to get this.

`ChatClientAgent` is also registered, but as the **inner unwrapped agent** — useful when you need access to `ChatClientAgent`-specific properties (`Instructions`, `Tools`, `ChatClient`) or want to mutate the tool list at runtime. **However, resolving `ChatClientAgent` bypasses both the `ConfigureAgent` middleware and the agent-level OpenTelemetry instrumentation.** Chat-level telemetry (`chat` span, `gen_ai.client.*` metrics) still emits because `IChatClient` is wrapped at registration time.

For most use cases — including the sample apps in this repo — inject `AIAgent`.

## License

This project is licensed under the Apache-2.0 License.

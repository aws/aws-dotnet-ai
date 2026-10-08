# AWS.Bedrock.MEAI

`AWS.Bedrock.MEAI` provides [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
implementations for [Amazon Bedrock](https://aws.amazon.com/bedrock/). It lets .NET developers use
Bedrock models through Microsoft's standard AI abstractions — `IChatClient`, `IEmbeddingGenerator`,
`IImageGenerator`, and `IRealtimeClient` — so Bedrock plugs into the broader .NET AI ecosystem
(agents, middleware, tooling) with no Bedrock-specific glue code.

> This package was previously published as `AWSSDK.Extensions.Bedrock.MEAI` from the
> [aws-sdk-net](https://github.com/aws/aws-sdk-net) repository. It now lives in
> [aws-dotnet-ai](https://github.com/aws/aws-dotnet-ai) alongside the other .NET AI packages and
> releases independently. The `AmazonBedrockRuntimeExtensions` methods (`AsIChatClient`,
> `AsIEmbeddingGenerator`, etc.) remain in the `Amazon.BedrockRuntime` namespace, so existing call
> sites need no code change — just update the package reference. The package's other types (such as
> `BedrockStructuredOutputMode`) live in the `AWS.Bedrock.MEAI` namespace; add `using AWS.Bedrock.MEAI;`
> only where you reference those directly.

## Installation

```
dotnet add package AWS.Bedrock.MEAI
```

## Getting started

Turn an `IAmazonBedrockRuntime` client into an `IChatClient`:

```csharp
using Amazon.BedrockRuntime;
using Microsoft.Extensions.AI;

IAmazonBedrockRuntime runtime = new AmazonBedrockRuntimeClient();

IChatClient chatClient = runtime.AsIChatClient("anthropic.claude-3-5-sonnet-20241022-v2:0");

var response = await chatClient.GetResponseAsync("What is Amazon Bedrock?");
Console.WriteLine(response);
```

Generate embeddings:

```csharp
IEmbeddingGenerator<string, Embedding<float>> generator =
    runtime.AsIEmbeddingGenerator("amazon.titan-embed-text-v2:0");

var embeddings = await generator.GenerateAsync(["Hello world"]);
```

## Features

- **Chat** (`AsIChatClient`) over the Bedrock Converse / ConverseStream APIs, including tool/function
  calling, multi-modal content (image, video, document), reasoning / extended thinking, citations,
  and prompt cache points.
- **Structured output** via JSON Schema, in two modes (`BedrockStructuredOutputMode`):
  - `SyntheticTool` (default) — works across the broadest set of models.
  - `Native` — uses Bedrock native structured outputs on models that support it; composes with
    user-provided tools and supports streaming.
- **Message coalescing** (opt-in) — pass a `BedrockChatClientOptions` with
  `CoalesceConsecutiveMessages = true` to `AsIChatClient` to combine consecutive request messages that
  map to the same Converse role (for example a tool-result message followed by a user message, both of
  which map to the Converse `user` role) into a single message, giving replayed histories a normalized
  request shape. Content-block order is preserved, messages separated by a cache point are never
  combined, and caller-supplied raw messages are not modified. Defaults to `false` (each message is
  sent separately). Optional client settings live on `BedrockChatClientOptions` so future options can be
  added without new `AsIChatClient` overloads.
- **Embeddings** (`AsIEmbeddingGenerator`).
- **Native stop reason and guardrail trace** surfaced on `ChatResponse.AdditionalProperties`
  (and the matching streaming updates) under the `AmazonBedrockRuntimeExtensions.StopReasonKey`
  (`"StopReason"`) and `TraceKey` (`"Trace"`) keys. Because multiple native stop reasons map to one
  `ChatFinishReason` (both `guardrail_intervened` and `content_filtered` map to `ContentFilter`), the
  `StopReason` string lets you tell them apart; `Trace` carries the guardrail trace as a `JsonElement`
  serialized from the SDK trace object, so it serializes safely when the response is persisted
  (including under trimming / Native AOT).
- **Image generation** (`AsIImageGenerator`) for Stability and Amazon Titan / Nova Canvas models.
  *(Experimental — `MEAI001`.)*
- **Realtime audio** (`AsIRealtimeClient`) for Amazon Nova Sonic bidirectional streaming, on
  `net8.0` and later. *(Experimental — `MEAI001`.)*

Some APIs are annotated `[Experimental("MEAI001")]` because they build on evaluation-stage
Microsoft.Extensions.AI abstractions. Suppress `MEAI001` to use them.

## Supported target frameworks

`net472`, `netstandard2.0`, and `net8.0`. The realtime (`IRealtimeClient`) APIs require `net8.0`
or later.

## Getting help

Open an issue on the [aws-dotnet-ai](https://github.com/aws/aws-dotnet-ai/issues) repository.

## License

Apache-2.0. See [LICENSE](https://github.com/aws/aws-dotnet-ai/blob/main/LICENSE).

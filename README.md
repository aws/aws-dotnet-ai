# AI Integrations on AWS for .NET

[![build status](https://img.shields.io/github/actions/workflow/status/aws/aws-dotnet-ai/aws-ci.yml?branch=dev)](https://github.com/aws/aws-dotnet-ai/actions/workflows/aws-ci.yml)

> **Developer Preview** — This project is in early preview and under active development. APIs may change between releases. Not recommended for production use.

This repository contains .NET libraries for building AI agents on AWS. Built on top of [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/) and [Amazon Bedrock AgentCore](https://docs.aws.amazon.com/bedrock-agentcore/latest/devguide/), these packages provide a zero-friction path from local development to production deployment.

## Packages

| Package | NuGet | Description |
| --- | --- | --- |
| [AWS.AgentCore.Hosting](./src/AWS.AgentCore.Hosting) | [![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.Hosting.svg)](https://www.nuget.org/packages/AWS.AgentCore.Hosting/) | Build AI agents that deploy to Amazon Bedrock AgentCore. Provides endpoint mapping, streaming, memory, middleware, NativeAOT support, and a source generator for zero-boilerplate development. |
| [AWS.AgentCore.Testing](./src/AWS.AgentCore.Testing) | [![nuget](https://img.shields.io/nuget/v/AWS.AgentCore.Testing.svg)](https://www.nuget.org/packages/AWS.AgentCore.Testing/) | Local development and testing emulators. Includes a Runtime Emulator, Memory Emulator, and Chat App UI — no AWS account required. |

## Quick Start

```
dotnet new web -n MyAgent
cd MyAgent
dotnet add package AWS.AgentCore.Hosting
```

See the [AWS.AgentCore.Hosting README](./src/AWS.AgentCore.Hosting/README.md) for complete getting-started instructions and code samples.

## Sample Applications

| Sample | Description |
| --- | --- |
| [AnnotationsSample](./sampleapps/AnnotationsSample) | Source generator with DI, tools, custom ping |
| [StreamingAgent](./sampleapps/StreamingAgent) | SSE streaming with extension methods |
| [MicrosoftAgentFrameworkSample](./sampleapps/MicrosoftAgentFrameworkSample) | Agent + function middleware, multiple tools |
| [NativeAotAnnotations](./sampleapps/NativeAotAnnotations) | NativeAOT with source generator |
| [NativeAotExtensions](./sampleapps/NativeAotExtensions) | NativeAOT with extension methods |
| [RemoteMcpAgent](./sampleapps/RemoteMcpAgent) | MCP tool provider integration |
| [AspireAppHost](./sampleapps/AspireAppHost) | Local dev experience with Aspire |

## Getting Help

For feature requests or issues using this library please open an [issue in this repository](https://github.com/aws/aws-dotnet-ai/issues).

## Contributing

We welcome community contributions and pull requests. See [CONTRIBUTING.md](./CONTRIBUTING.md) for information on how to submit code.

## Security

See [CONTRIBUTING](CONTRIBUTING.md#security-issue-notifications) for more information.

## License

This project is licensed under the Apache-2.0 License.

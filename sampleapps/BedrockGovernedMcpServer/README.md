# BedrockGovernedMcpServer

A minimal MCP server governed by [Microsoft's Agent Governance Toolkit](https://devblogs.microsoft.com/dotnet/announcing-agent-governance-toolkit-mcp-extensions-for-dotnet/) with the AWS backends from [`AWS.Bedrock.MAG`](../../src/AWS.Bedrock.MAG) layered on top. It shows the package's primary path:

```csharp
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithGovernance(...)          // Microsoft.AgentGovernance.Extensions.ModelContextProtocol
    .WithBedrockGovernance(...)   // AWS.Bedrock.MAG
    .WithTools<SupportTools>();
```

- `.WithGovernance(...)` (Microsoft's MCP extensions package) loads the YAML policy and stands up the toolkit's `GovernanceKernel`.
- `.WithBedrockGovernance(...)` (this repo) attaches the Bedrock Guardrails **policy backend**, Bedrock **PII sanitization** of tool output, and the **CloudWatch audit** sink to that kernel.

The server exposes two toy "customer support" tools (`Tools/SupportTools.cs`). `lookup_customer` deliberately returns PII so you can watch sanitization redact it.

## Two modes

The sample picks a mode from the environment so it runs with or without a pre-created guardrail:

| `MAG_GUARDRAIL_ID` | Policy | PII sanitization | AWS resource needed |
|---|---|---|---|
| **unset** (default) | Inline checks (`InvokeGuardrailChecks`) — PII detection on the tool-call arguments | off (inline checks detect but don't mask) | none — just IAM |
| **set** to a guardrail id | `ApplyGuardrail` on the tool-call input | on — redacts PII in tool output using the same guardrail | a Bedrock guardrail |

## Prerequisites

- .NET 8 SDK.
- AWS credentials in the default chain (env vars, profile, or role). Region defaults to `us-west-2` (override with `MAG_TEST_REGION`).
- IAM for the mode you run:
  - Inline-checks mode: `bedrock:InvokeGuardrailChecks`.
  - Guardrail mode: `bedrock:ApplyGuardrail` on the guardrail, plus `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents` (and `cloudwatch:PutMetricData` if metrics are on) for audit.

## Run it

```bash
cd sampleapps/BedrockGovernedMcpServer
dotnet run
```

It's a stdio MCP server, so it talks JSON-RPC over stdin/stdout and logs to stderr. Point any MCP client at `dotnet run` in this directory, or drive it by hand — send an `initialize`, then a `tools/call`:

```jsonc
// benign call -> allowed, tool runs
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"lookup_customer","arguments":{"customerId":"C-1024"}}}

// argument contains an SSN -> denied by the Bedrock policy backend
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"lookup_customer","arguments":{"customerId":"my ssn is 123-45-6789"}}}
```

In default (inline-checks) mode the first returns the customer record and the second comes back with `isError: true` and `"External policy backend 'bedrock-guardrails' denied the request."`

To exercise the full path (guardrail policy + output PII redaction + CloudWatch audit), create a Bedrock guardrail that anonymizes `US_SOCIAL_SECURITY_NUMBER` and run with `MAG_GUARDRAIL_ID=<id> dotnet run`. The `lookup_customer` output SSN then comes back redacted.

## Two things worth knowing (learned building this sample)

1. **`WithGovernance` requires an authenticated agent identity by default.** With it on, every call is denied before reaching Bedrock. This sample sets `RequireAuthenticatedAgentId = false` to use the `DefaultAgentId` fallback. In production, leave it on and set an `AgentIdResolver` that maps your authenticated principals.
2. **Prompt-attack / content-filter inline checks false-positive on the JSON tool-call context.** The tool-call context is serialized to compact JSON by default, and the prompt-attack classifier reads that structure as an injection attempt — denying benign calls. This sample uses PII inline checks (which score cleanly on JSON). To use prompt-attack or content-filter categories, project the context to prose first via `Policy.ContextSerializer` (see the commented example in `Program.cs`).

## Package versions

`AWS.Bedrock.MAG` pins `ModelContextProtocol` 2.1.0; `Microsoft.AgentGovernance.Extensions.ModelContextProtocol` 5.0.0 builds against 1.4.1. NuGet unifies to 2.1.0 and the two compose on the same `IMcpServerBuilder` — verified by this sample building and running end-to-end.

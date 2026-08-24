# AWS.Bedrock.MAG

AWS backends for [Microsoft's Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit). This package plugs AWS managed services into the toolkit's extension points so a .NET AI agent can pick up Bedrock-backed policy, PII sanitization, and durable audit with a few lines of configuration.

- **Bedrock Guardrails policy backend**: ML policy evaluation added alongside the toolkit's rule, OPA, and Cedar backends. Fails closed on error.
- **CloudWatch audit sink**: writes governance events to CloudWatch Logs with aggregated metrics.
- **Bedrock Guardrails PII sanitization**: redacts or blocks 30+ PII entity types in MCP tool output.

> Preview (0.1.0). The API may change while the toolkit's extension surface stabilizes. Remaining features (high-level MCP/DI entry points, inline guardrail checks) ship in follow-up preview releases.

## Install

```
dotnet add package AWS.Bedrock.MAG
```

Targets net8.0. Requires `Microsoft.AgentGovernance` 5.0.0.

## Use it

### Imperative (you already hold a kernel)

Attach the policy backend and audit sink directly to a `GovernanceKernel` you constructed:

```csharp
var kernel = new GovernanceKernel(new GovernanceOptions { PolicyPaths = { "policies/default.yaml" } });

kernel.PolicyEngine.AddExternalBackend(
    new BedrockGuardrailsPolicyBackend(new BedrockGuardrailsPolicyOptions
    {
        GuardrailId = "gr-abc123",
    }));

using var audit = new CloudWatchAuditSink(new CloudWatchAuditOptions
{
    LogGroupName    = "/agent-governance/audit",
    MetricNamespace = "AgentGovernance/Bedrock",
});
audit.Subscribe(kernel.AuditEmitter);
```

### PII sanitization on MCP tool output

`BedrockGuardrailsSanitizer` runs the ANONYMIZE action on the text blocks of an MCP tool result. The MCP server wiring that plugs it in for you (via `WithBedrockGovernance`) lands in the next preview; in the meantime you can construct the sanitizer directly to feed it tool-result text:

```csharp
var sanitizer = new BedrockGuardrailsSanitizer(new BedrockSanitizationOptions
{
    GuardrailId = "gr-abc123",
});

var scrubbed = await sanitizer.SanitizeAsync(rawToolText, cancellationToken);
```

## Required IAM

The credentials the agent runs under need:

- `bedrock:ApplyGuardrail` on the guardrail (policy backend and PII sanitization).
- `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents` on the audit log group (audit sink).
- `cloudwatch:PutMetricData` (audit metrics, when `EmitMetrics` is on).

The audit sink uses [AWS.Logger.Core](https://github.com/aws/aws-logging-dotnet), which creates the log group and stream on first use, so the `logs:Create*` permissions are required.

## Limitations

PII sanitization covers the **text** blocks of an MCP tool result, matching the toolkit's own sanitizer. A tool result's `StructuredContent` (structured JSON) is passed through unchanged. If a tool returns PII in `StructuredContent`, mirror it into a text block so the guardrail sees it. Sanitizing arbitrary structured output is a post-v1 follow-up.

## Cost

This package calls billed AWS services: Bedrock Guardrails (priced per text unit evaluated, once for policy on the input and once for PII on the output), CloudWatch Logs ingestion and storage, and CloudWatch custom metrics. Tune `FlushInterval` to trade audit latency against request volume.

## License

Apache-2.0.

# AWS.Bedrock.MAG

AWS backends for [Microsoft's Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit). This package plugs AWS managed services into the toolkit's existing extension points, so a .NET AI agent gets Bedrock-backed policy, PII sanitization, and durable audit with about two lines of configuration.

- **Bedrock Guardrails policy backend**: ML policy evaluation added alongside the toolkit's rule, OPA, and Cedar backends. Use a pre-created guardrail (`GuardrailId`) or define checks inline (`InlineChecks`) with no guardrail resource to manage. Fails closed on error.
- **Bedrock Guardrails PII sanitization**: redacts or blocks 30+ PII entity types in MCP tool output.
- **CloudWatch audit sink**: writes governance events to CloudWatch Logs with aggregated metrics.

> Preview (0.1.0). The API may change while the toolkit's extension surface stabilizes.

## Install

```
dotnet add package AWS.Bedrock.MAG
```

Targets net8.0. Requires `Microsoft.AgentGovernance` 5.0.0.

## Use it

### MCP server (primary)

Add `WithBedrockGovernance` after the toolkit's `WithGovernance`:

```csharp
builder.Services.AddMcpServer()
    .WithGovernance(o => o.PolicyPaths.Add("policies/default.yaml"))  // Microsoft's toolkit
    .WithBedrockGovernance(o =>                                        // this package
    {
        o.Policy.GuardrailId    = "gr-abc123";
        o.EnablePiiSanitization = true;                               // ANONYMIZE tool output
        o.Audit.LogGroupName    = "/agent-governance/audit";
    });
```

### Non-MCP agent (plain DI)

```csharp
builder.Services.AddBedrockGovernance(o =>
{
    o.Policy.GuardrailId    = "gr-abc123";
    o.Audit.MetricNamespace = "AgentGovernance/Bedrock";
});
// Requires a GovernanceKernel already in DI (from the toolkit).
```

### Imperative (you already hold a kernel)

```csharp
var kernel = new GovernanceKernel(new GovernanceOptions { PolicyPaths = { "policies/default.yaml" } });

kernel.AddBedrockGuardrailsPolicy(o => o.GuardrailId = "gr-abc123");
using var audit = kernel.AddCloudWatchAudit(o => o.LogGroupName = "/agent-governance/audit");
```

### Inline guardrail checks (no pre-created guardrail)

When you don't want to manage a Bedrock guardrail resource, define the checks inline. The policy backend calls `InvokeGuardrailChecks` with the categories/entities from the request; a check trips the deny when its score meets the configured threshold. Detection only — this mode does not mask text, so PII sanitization still needs `GuardrailId`.

```csharp
builder.Services.AddBedrockGovernance(o =>
{
    o.EnablePiiSanitization = false;
    o.Policy.InlineChecks   = new GuardrailChecksOptions
    {
        ContentFilterCategories      = { "HATE", "INSULTS", "VIOLENCE" },
        PromptAttackCategories       = { "PROMPT_INJECTION", "JAILBREAK" },
        SensitiveInformationEntities = { "US_SOCIAL_SECURITY_NUMBER", "EMAIL" },
        SeverityThreshold            = 0.5,   // content filter + prompt attack
        ConfidenceThreshold          = 0.5,   // PII
    };
});
```

`InlineChecks` composes with every entry point (`WithBedrockGovernance`, `AddBedrockGovernance`, and the imperative `AddBedrockGuardrailsPolicy`); set either `GuardrailId` or `InlineChecks`. When both are set the pre-created guardrail wins.

## Required IAM

The credentials the agent runs under need:

- `bedrock:ApplyGuardrail` on the guardrail (policy and PII sanitization).
- `bedrock:InvokeGuardrailChecks` (inline-checks policy mode; no guardrail resource is referenced).
- `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents` on the audit log group (audit sink).
- `cloudwatch:PutMetricData` (audit metrics, when `EmitMetrics` is on).

The audit sink uses [AWS.Logger.Core](https://github.com/aws/aws-logging-dotnet), which creates the log group and stream on first use, so the `logs:Create*` permissions are required.

## Limitations

PII sanitization covers the **text** blocks of an MCP tool result, matching the toolkit's own sanitizer. A tool result's `StructuredContent` (structured JSON) is passed through unchanged. If a tool returns PII in `StructuredContent`, mirror it into a text block so the guardrail sees it. Sanitizing arbitrary structured output is a post-v1 follow-up.

## Cost

This package calls billed AWS services: Bedrock Guardrails (priced per text unit evaluated, once for policy on the input and once for PII on the output; inline checks are billed on the same per-text-unit basis), CloudWatch Logs ingestion and storage, and CloudWatch custom metrics. Tune `FlushInterval` to trade audit latency against request volume.

## License

Apache-2.0.

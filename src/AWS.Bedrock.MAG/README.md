# AWS.Bedrock.MAG

AWS backends for [Microsoft's Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit). This package plugs AWS managed services into the toolkit's extension points so a .NET AI agent can pick up Bedrock-backed policy, PII sanitization, and durable audit with a few lines of configuration.

- **Bedrock Guardrails policy backend**: ML policy evaluation added alongside the toolkit's rule, OPA, and Cedar backends. Fails closed on error.

> Preview (0.1.0). The API may change while the toolkit's extension surface stabilizes. Remaining features (CloudWatch audit sink, PII sanitization, high-level MCP/DI entry points, inline guardrail checks) ship in follow-up preview releases.

## Install

```
dotnet add package AWS.Bedrock.MAG
```

Targets net8.0. Requires `Microsoft.AgentGovernance` 5.0.0.

## Use it

### Imperative (you already hold a kernel)

Attach the policy backend directly to a `GovernanceKernel` you constructed:

```csharp
var kernel = new GovernanceKernel(new GovernanceOptions { PolicyPaths = { "policies/default.yaml" } });

kernel.PolicyEngine.AddExternalBackend(
    new BedrockGuardrailsPolicyBackend(new BedrockGuardrailsPolicyOptions
    {
        GuardrailId = "gr-abc123",
    }));
```

The high-level MCP and DI entry points (`WithBedrockGovernance` / `AddBedrockGovernance` / `AddBedrockGuardrailsPolicy`) that wire this up for you land in a follow-up preview release.

## Required IAM

The credentials the agent runs under need:

- `bedrock:ApplyGuardrail` on the guardrail evaluated by the policy backend.

## Cost

The policy backend calls Bedrock Guardrails (billed per text unit evaluated) on every governed tool call.

## License

Apache-2.0.

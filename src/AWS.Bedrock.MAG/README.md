# AWS.Bedrock.MAG

AWS backends for [Microsoft's Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit). This package plugs AWS managed services into the toolkit's extension points so a .NET AI agent can pick up Bedrock-backed policy, PII sanitization, and durable audit with a few lines of configuration.

> Preview (0.1.0). The API may change while the toolkit's extension surface stabilizes.

This first drop is the package scaffold and a shared internal guardrail client. Individual features are added in follow-up PRs and documented as each ships:

1. Bedrock Guardrails policy backend.
2. CloudWatch audit sink.
3. Bedrock Guardrails PII sanitization for MCP tool output.
4. High-level entry points (MCP `WithBedrockGovernance`, DI `AddBedrockGovernance`, imperative kernel extensions).
5. Inline guardrail checks (no pre-created guardrail).

## Install

```
dotnet add package AWS.Bedrock.MAG
```

Targets net8.0.

## Required IAM

- `bedrock:ApplyGuardrail` on any guardrail the internal client is asked to evaluate.

## License

Apache-2.0.

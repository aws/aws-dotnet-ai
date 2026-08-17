# AWS.AgentGovernance.Bedrock

AWS backends for [Microsoft's Agent Governance Toolkit](https://github.com/microsoft/agent-governance-toolkit). This package plugs AWS managed services into the toolkit's existing extension points:

- **Bedrock Guardrails policy backend**: ML policy evaluation alongside the toolkit's rule, OPA, and Cedar engines.
- **Bedrock Guardrails PII sanitization**: redacts or blocks 30+ PII entity types in MCP tool output.
- **CloudWatch audit sink**: writes governance events to CloudWatch Logs with metrics.

> Preview (0.1.0). The API may change while the toolkit's extension surface stabilizes.

## Status

This package is under active development. See the repository for the current design and progress.

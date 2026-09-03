// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

// Sample: an MCP server governed by Microsoft's Agent Governance Toolkit with the AWS Bedrock backends
// layered on top. It follows the primary path from the AWS.Bedrock.MAG README:
//
//     AddMcpServer().WithGovernance(...).WithBedrockGovernance(...)
//
// .WithGovernance(...)        comes from Microsoft.AgentGovernance.Extensions.ModelContextProtocol and
//                             registers the toolkit's GovernanceKernel + policy engine on the MCP server.
// .WithBedrockGovernance(...) comes from AWS.Bedrock.MAG and attaches the Bedrock Guardrails policy
//                             backend, Bedrock PII sanitization, and the CloudWatch audit sink.
//
// See README.md for prerequisites, IAM, and how to drive it.

using Amazon;
using AgentGovernance.Extensions.ModelContextProtocol;
using BedrockGovernedMcpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// A stdio MCP server speaks the protocol over stdout, so all logging MUST go to stderr — otherwise log
// lines corrupt the protocol stream.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Region and guardrail come from the environment so the sample stays runnable without editing code.
var region = RegionEndpoint.GetBySystemName(
    Environment.GetEnvironmentVariable("MAG_TEST_REGION") ?? "us-west-2");
var guardrailId = Environment.GetEnvironmentVariable("MAG_GUARDRAIL_ID");

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // Microsoft's toolkit: loads YAML policy and stands up the GovernanceKernel this server governs with.
    .WithGovernance(o =>
    {
        // Resolve against the assembly base dir (not the launcher's cwd) so the csproj's
        // CopyToOutputDirectory copy is the file that loads, regardless of where an MCP client
        // launches the built binary from.
        o.PolicyPaths.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "policies", "mcp.yaml"));
        o.ServerName = "bedrock-governed-sample";
        o.DefaultAgentId = "did:mcp:sample-agent";

        // The toolkit requires an authenticated agent identity by default and denies every call before it
        // reaches the Bedrock backend. For a self-contained sample we allow the DefaultAgentId fallback.
        // In production, leave this on and set an AgentIdResolver that maps your authenticated principals.
        o.RequireAuthenticatedAgentId = false;
    })
    // AWS backends. Two modes, chosen by whether you supply a pre-created guardrail:
    //   MAG_GUARDRAIL_ID set   -> full path: guardrail-based policy + PII sanitization of tool output + audit.
    //   MAG_GUARDRAIL_ID unset -> inline-checks policy (InvokeGuardrailChecks, no guardrail resource) with
    //                             PII sanitization off (inline checks detect but do not mask text) and audit
    //                             off, so the default mode needs no AWS resources beyond IAM.
    .WithBedrockGovernance(o =>
    {
        o.Region = region;
        o.Audit.LogGroupName = "/agent-governance/bedrock-sample";

        if (!string.IsNullOrWhiteSpace(guardrailId))
        {
            o.Policy.GuardrailId = guardrailId;   // ApplyGuardrail on tool-call input.
            o.EnablePiiSanitization = true;       // Reuses the policy guardrail to redact tool-output PII.
        }
        else
        {
            // Inline-checks mode is the "minimal, no AWS resources" default. EnableAudit defaults to true
            // and would otherwise spin up the CloudWatch sink (logs:CreateLogGroup/CreateLogStream/
            // PutLogEvents, cloudwatch:PutMetricData) even here — turn it off so the default mode truly
            // needs only bedrock:InvokeGuardrailChecks, as the README advertises.
            o.EnableAudit = false;

            o.EnablePiiSanitization = false;
            o.Policy.InlineChecks = new AWS.Bedrock.MAG.GuardrailChecksOptions
            {
                // PII detection on the (JSON) tool-call context: a call whose arguments contain an SSN or
                // email is denied. This is the reliable inline-check demo.
                SensitiveInformationEntities = { "US_SOCIAL_SECURITY_NUMBER", "EMAIL" },
                ConfidenceThreshold = 0.5,

                // NOTE: content-filter and prompt-attack categories are intentionally left off here. By
                // default the tool-call context is serialized to compact JSON, and the prompt-attack
                // classifier reads that structured JSON as an injection attempt and denies benign calls. To
                // use those categories, project the context to prose first via Policy.ContextSerializer, e.g.:
                //   o.Policy.ContextSerializer = ctx => $"Tool {ctx["tool"]} called with {string.Join(", ", ctx)}";
                // and then add:
                //   PromptAttackCategories = { "PROMPT_INJECTION", "JAILBREAK" },
                //   ContentFilterCategories = { "HATE", "INSULTS", "VIOLENCE" }, SeverityThreshold = 0.5,
            };
        }
    })
    .WithTools<SupportTools>();

await builder.Build().RunAsync();

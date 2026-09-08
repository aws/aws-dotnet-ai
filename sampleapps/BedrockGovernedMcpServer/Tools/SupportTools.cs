// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace BedrockGovernedMcpServer.Tools;

/// <summary>
/// A tiny "customer support" MCP tool surface used to exercise the two Bedrock governance paths:
/// <list type="bullet">
///   <item>Policy evaluation runs on the tool-call <b>input</b> (tool name + arguments), so passing a
///   blocked word or PII in an argument lets the Bedrock policy backend deny the call.</item>
///   <item>PII sanitization runs on the tool-call <b>output</b> text, so <see cref="LookupCustomer"/>
///   deliberately returns an SSN to show it redacted (when a guardrail is configured).</item>
/// </list>
/// </summary>
[McpServerToolType]
public sealed class SupportTools
{
    [McpServerTool(Name = "lookup_customer")]
    [Description("Look up a customer's contact record by their customer id.")]
    public string LookupCustomer(
        [Description("The customer id, e.g. \"C-1024\".")] string customerId)
    {
        // Returns PII in the text block on purpose: with a guardrail configured the Bedrock sanitizer
        // redacts the SSN before this reaches the caller. See the README for what to expect.
        return $"Customer {customerId}: Jane Doe, jane.doe@example.com, SSN 123-45-6789, status ACTIVE.";
    }

    [McpServerTool(Name = "get_account_balance")]
    [Description("Get the current account balance for a customer.")]
    public string GetAccountBalance(
        [Description("The customer id, e.g. \"C-1024\".")] string customerId)
    {
        return $"Customer {customerId} balance: $482.10 USD.";
    }
}

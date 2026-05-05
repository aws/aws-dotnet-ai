// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace BuildDiagnosticAgent.Heuristics;

public static class CodebaseHeuristics
{
    public const string Patterns = """
        Codebase-specific heuristics for aws-dotnet-ai. Apply when relevant; don't force-fit.

        - Source-generator tests (AWS.AgentCore.SourceGenerator.Tests) failing with
          "incremental generator did not produce expected output" usually mean the PR
          changed an attribute, analyzer, or partial-class shape. Read obj/Generated/
          (or the snapshot file referenced in the test) and the changed attribute file
          before concluding.

        - NativeAOT tests (sampleapps/NativeAot* projects) failing with trim warnings
          (IL2026, IL2104, IL3050) usually indicate reflection in newly added code.
          Suggested fix: annotate the call site with [DynamicallyAccessedMembers] or
          register the type with a json source-generator.

        - BedrockChatClient or Microsoft.Extensions.AI tests failing with serialization
          errors are commonly caused by an upstream package version bump. Check the
          .csproj diff for PackageReference Version changes.

        - Integration tests (test/AWS.AgentCore.IntegrationTests/) timing out at
          startup usually mean either (a) test resources weren't seeded by the
          deployed CloudFormation template, or (b) the test-runner account is under
          load. If the same test passed in the last 3 unrelated runs, prefer (b).

        - xUnit assertions of the form "Expected: <expected>; Actual: null" in agent
          handler tests typically point at AgentCoreRuntimeContext binding failure;
          the test fixture probably forgot to set x-amzn-bedrock-agentcore-* headers.
        """;
}

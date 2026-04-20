// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Marks a method as the AgentCore agent handler for <c>POST /invocations</c>.
/// The containing class is resolved from DI, so constructor-injected dependencies are available.
/// <para>
/// The return type determines the response format:
/// <c>Task&lt;string&gt;</c> for JSON, <c>IAsyncEnumerable&lt;string&gt;</c> for SSE streaming.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AgentCoreHandlerAttribute : Attribute { }

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Marks a class as the AgentCore startup configuration class.
/// The source generator looks for a <c>ConfigureServices(WebApplicationBuilder)</c> method
/// on this class to allow users to register services and configure the application.
/// </summary>
/// <example>
/// <code>
/// [AgentCoreStartup]
/// public class Startup
/// {
///     public void ConfigureServices(WebApplicationBuilder builder)
///     {
///         builder.AddAgentCore(options =>
///         {
///             options.ModelId = "anthropic.claude-sonnet-4-20250514-v1:0";
///         });
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class AgentCoreStartupAttribute : Attribute { }

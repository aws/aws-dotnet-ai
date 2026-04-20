// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Marks a method as the custom <c>GET /ping</c> health-check handler.
/// When present, the generated code uses this method instead of the default ping response.
/// The method should return an object that will be serialized as JSON.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AgentCorePingAttribute : Attribute { }

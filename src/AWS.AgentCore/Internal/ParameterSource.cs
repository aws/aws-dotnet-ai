// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.Internal;

/// <summary>
/// Identifies where a handler parameter's value comes from at request time.
/// </summary>
internal enum ParameterSource
{
    /// <summary>Deserialized from the JSON request body as <c>TRequest</c>.</summary>
    Request,

    /// <summary>Populated from AgentCore HTTP headers.</summary>
    RuntimeContext,

    /// <summary>Bound to <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestAborted"/>.</summary>
    CancellationToken,

    /// <summary>The raw <see cref="Microsoft.AspNetCore.Http.HttpContext"/> for the current request.</summary>
    HttpContext,

    /// <summary>Resolved from the DI container via <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestServices"/>.</summary>
    Service
}

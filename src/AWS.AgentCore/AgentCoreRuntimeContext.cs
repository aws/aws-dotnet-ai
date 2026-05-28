// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore;

/// <summary>
/// Provides typed access to the AgentCore Runtime HTTP headers injected into each
/// <c>/invocations</c> request. When included as a parameter in a
/// <c>MapAgentCore</c> handler,
/// it is automatically populated from the incoming request headers.
/// </summary>
public class AgentCoreRuntimeContext
{
    /// <summary>Session identifier from <c>X-Amzn-Bedrock-AgentCore-Runtime-Session-Id</c>.</summary>
    public string? SessionId { get; set; }

    /// <summary>Request identifier from <c>X-Amzn-Bedrock-AgentCore-Runtime-Request-Id</c>.
    /// Auto-generated as a new GUID if the header is not present.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Workload access token from <c>WorkloadAccessToken</c>.</summary>
    public string? AccessToken { get; set; }

    /// <summary>OAuth2 callback URL from <c>OAuth2CallbackUrl</c>.</summary>
    public string? OAuth2CallbackUrl { get; set; }

    /// <summary>Authorization header value.</summary>
    public string? Authorization { get; set; }

    /// <summary>
    /// Custom headers whose names start with <c>X-Amzn-Bedrock-AgentCore-Runtime-Custom-</c>,
    /// with the prefix stripped from the key.
    /// </summary>
    public Dictionary<string, string> CustomHeaders { get; set; } = new();

    /// <summary>
    /// All HTTP headers from the incoming request, exposed as a read-only dictionary
    /// for accessing any header not covered by the typed properties above.
    /// </summary>
    public IReadOnlyDictionary<string, string> AllHeaders { get; set; } = new Dictionary<string, string>();

    internal static AgentCoreRuntimeContext FromHttpContext(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        var headers = httpContext.Request.Headers;
        var context = new AgentCoreRuntimeContext
        {
            SessionId = headers["X-Amzn-Bedrock-AgentCore-Runtime-Session-Id"].FirstOrDefault(),
            RequestId = headers["X-Amzn-Bedrock-AgentCore-Runtime-Request-Id"].FirstOrDefault()
                ?? headers["x-amzn-requestid"].FirstOrDefault()
                ?? Guid.NewGuid().ToString(),
            AccessToken = headers["WorkloadAccessToken"].FirstOrDefault(),
            OAuth2CallbackUrl = headers["OAuth2CallbackUrl"].FirstOrDefault(),
            Authorization = headers["Authorization"].FirstOrDefault(),
        };

        // Collect all headers into a flat dictionary
        var allHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            allHeaders[header.Key] = header.Value.ToString();
        }
        context.AllHeaders = allHeaders;

        const string customPrefix = "X-Amzn-Bedrock-AgentCore-Runtime-Custom-";
        foreach (var header in headers)
        {
            if (header.Key.StartsWith(customPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = header.Key[customPrefix.Length..];
                context.CustomHeaders[key] = header.Value.ToString();
            }
        }

        return context;
    }
}

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Runtime;
using Amazon.Runtime.Internal;

namespace AWS.AgentCore.Internal;

/// <summary>
/// Appends a custom user-agent component to all outgoing AWS SDK requests
/// made through AgentCore-registered service clients. This enables tracking
/// of library adoption and feature usage via AWS service-side telemetry.
/// </summary>
internal static partial class UserAgentTelemetry
{
    internal static readonly string UserAgentString =
        $"lib/aws-dotnet-ai#{AssemblyVersion}";

    private static readonly HashSet<int> _registeredClients = new();

    /// <summary>
    /// Registers the user-agent handler on a specific client instance.
    /// Safe to call multiple times for the same client — only attaches once.
    /// </summary>
    internal static void RegisterWith(AmazonServiceClient client)
    {
        var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(client);
        if (!_registeredClients.Add(id))
            return;

        client.BeforeRequestEvent += BeforeRequestHandler;
    }

    private static void BeforeRequestHandler(object sender, RequestEventArgs e)
    {
        if (e is WebServiceRequestEventArgs { Request: IAmazonWebServiceRequest internalRequest })
        {
            if (!internalRequest.UserAgentDetails.GetCustomUserAgentComponents().Contains(UserAgentString))
            {
                internalRequest.UserAgentDetails.AddUserAgentComponent(UserAgentString);
            }
        }
    }
}

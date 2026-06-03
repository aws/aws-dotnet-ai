// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Runtime;
using Amazon.Runtime.Internal;

namespace AWS.AgentCore.Internal;

/// <summary>
/// Appends a custom user-agent component to all outgoing AWS SDK requests
/// made by any service client in the process. Uses the global
/// <see cref="RuntimePipelineCustomizerRegistry"/> so it applies to all clients —
/// including those registered by the user — without requiring per-client instrumentation.
/// </summary>
internal static partial class UserAgentTelemetry
{
    internal static readonly string UserAgentString =
        $"lib/aws-dotnet-ai#{AssemblyVersion}";

    private static volatile bool _initialized;

    /// <summary>
    /// Registers the user-agent pipeline customizer globally. All AWS SDK clients
    /// created after this call will include the AgentCore user-agent component.
    /// Safe to call multiple times — only registers once.
    /// </summary>
    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        RuntimePipelineCustomizerRegistry.Instance.Register(new UserAgentPipelineCustomizer());
    }

    private sealed class UserAgentPipelineCustomizer : IRuntimePipelineCustomizer
    {
        public string UniqueName => "AWSAgentCoreUserAgent";

        public void Customize(Type serviceClientType, RuntimePipeline pipeline)
        {
            pipeline.AddHandlerAfter<Marshaller>(new UserAgentHandler());
        }
    }

    private sealed class UserAgentHandler : PipelineHandler
    {
        public override void InvokeSync(IExecutionContext executionContext)
        {
            AddUserAgent(executionContext);
            base.InvokeSync(executionContext);
        }

        public override async Task<T> InvokeAsync<T>(IExecutionContext executionContext)
        {
            AddUserAgent(executionContext);
            return await base.InvokeAsync<T>(executionContext);
        }

        private static void AddUserAgent(IExecutionContext executionContext)
        {
            if (executionContext.RequestContext.OriginalRequest is IAmazonWebServiceRequest request)
            {
                if (!request.UserAgentDetails.GetCustomUserAgentComponents().Contains(UserAgentString))
                {
                    request.UserAgentDetails.AddUserAgentComponent(UserAgentString);
                }
            }
        }
    }
}

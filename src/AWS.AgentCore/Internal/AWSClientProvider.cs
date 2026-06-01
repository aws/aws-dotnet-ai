// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.Runtime;

namespace AWS.AgentCore.Internal;

/// <summary>
/// Provides AWS service clients from the DI container with user-agent telemetry attached.
/// All internal code that needs an AWS SDK client should go through this provider
/// rather than resolving directly from DI.
/// </summary>
internal sealed class AWSClientProvider
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="AWSClientProvider"/>.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider used to resolve AWS SDK clients.</param>
    public AWSClientProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Resolves an AWS service client from the DI container and attaches user-agent telemetry.
    /// </summary>
    /// <typeparam name="T">The AWS service interface type (e.g., <c>IAmazonBedrockAgentCore</c>).</typeparam>
    /// <returns>The resolved service client with telemetry registered.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no service client of type <typeparamref name="T"/> is registered in DI.
    /// </exception>
    public T GetServiceClient<T>() where T : IAmazonService
    {
        var service = _serviceProvider.GetService(typeof(T))
            ?? throw new InvalidOperationException(
                $"No AWS service client of type {typeof(T).Name} is registered in DI. " +
                $"Register it via AddAWSService<{typeof(T).Name}>() or provide one explicitly.");

        if (service is AmazonServiceClient client)
        {
            UserAgentTelemetry.RegisterWith(client);
        }

        return (T)service;
    }
}

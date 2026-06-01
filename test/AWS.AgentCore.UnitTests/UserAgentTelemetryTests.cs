// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.BedrockAgentCore;
using Amazon.Runtime;
using AWS.AgentCore.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace AWS.AgentCore.UnitTests;

public class UserAgentTelemetryTests
{
    [Fact]
    public void UserAgentString_ContainsLibraryIdentifier()
    {
        Assert.StartsWith("lib/aws-dotnet-ai#", UserAgentTelemetry.UserAgentString);
    }

    [Fact]
    public void UserAgentString_ContainsVersion()
    {
        var parts = UserAgentTelemetry.UserAgentString.Split('#');
        Assert.Equal(2, parts.Length);

        var version = parts[1];
        Assert.False(string.IsNullOrEmpty(version));
        Assert.NotEqual("0.0.0", version);
    }

    [Fact]
    public void RegisterWith_DoesNotThrow()
    {
        var client = CreateTestClient();
        UserAgentTelemetry.RegisterWith(client);
    }

    [Fact]
    public void RegisterWith_SameClientTwice_DoesNotThrow()
    {
        var client = CreateTestClient();
        UserAgentTelemetry.RegisterWith(client);
        UserAgentTelemetry.RegisterWith(client);
    }

    [Fact]
    public void AWSClientProvider_GetServiceClient_ReturnsRegisteredClient()
    {
        var client = CreateTestClient();
        var services = new ServiceCollection();
        services.AddSingleton<IAmazonBedrockAgentCore>(client);
        var sp = services.BuildServiceProvider();

        var provider = new AWSClientProvider(sp);
        var resolved = provider.GetServiceClient<IAmazonBedrockAgentCore>();

        Assert.Same(client, resolved);
    }

    [Fact]
    public void AWSClientProvider_GetServiceClient_ThrowsWhenNotRegistered()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var provider = new AWSClientProvider(sp);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetServiceClient<IAmazonBedrockAgentCore>());

        Assert.Contains("IAmazonBedrockAgentCore", ex.Message);
    }

    [Fact]
    public void AWSClientProvider_GetServiceClient_CalledMultipleTimes_ReturnsSameClient()
    {
        var client = CreateTestClient();
        var services = new ServiceCollection();
        services.AddSingleton<IAmazonBedrockAgentCore>(client);
        var sp = services.BuildServiceProvider();

        var provider = new AWSClientProvider(sp);
        var first = provider.GetServiceClient<IAmazonBedrockAgentCore>();
        var second = provider.GetServiceClient<IAmazonBedrockAgentCore>();

        Assert.Same(first, second);
    }

    private static AmazonBedrockAgentCoreClient CreateTestClient()
    {
        return new AmazonBedrockAgentCoreClient(
            new AnonymousAWSCredentials(),
            new AmazonBedrockAgentCoreConfig
            {
                ServiceURL = "http://localhost:9999"
            });
    }
}

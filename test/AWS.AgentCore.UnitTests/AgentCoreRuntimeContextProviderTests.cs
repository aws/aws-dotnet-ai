// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.UnitTests;

public class AgentCoreRuntimeContextProviderTests
{
    [Fact]
    public void ContextKey_IsCorrectlyDefined()
    {
        Assert.Equal("AgentCore.RuntimeContext", AgentCoreRuntimeContextProvider.ContextKey);
    }

    [Fact]
    public void Provider_CanBeInstantiated()
    {
        var provider = new AgentCoreRuntimeContextProvider();
        Assert.NotNull(provider);
    }
}

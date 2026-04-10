// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.AgentCore.UnitTests;

public class AgentCoreOptionsTests
{
    [Fact]
    public void DefaultModelId_IsClaudeSonnet()
    {
        var options = new AgentCoreOptions();

        Assert.Equal("anthropic.claude-sonnet-4-20250514-v1:0", options.ModelId);
    }

    [Fact]
    public void DefaultPort_Is8080()
    {
        var options = new AgentCoreOptions();

        Assert.Equal(8080, options.Port);
    }

    [Fact]
    public void ModelId_CanBeOverridden()
    {
        var options = new AgentCoreOptions
        {
            ModelId = "custom-model-id"
        };

        Assert.Equal("custom-model-id", options.ModelId);
    }

    [Fact]
    public void Port_CanBeOverridden()
    {
        var options = new AgentCoreOptions
        {
            Port = 9090
        };

        Assert.Equal(9090, options.Port);
    }
}

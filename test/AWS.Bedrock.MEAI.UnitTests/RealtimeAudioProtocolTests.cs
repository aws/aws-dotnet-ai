// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using Xunit;

namespace AWS.Bedrock.MEAI;

public class RealtimeAudioProtocolTests
{
    [Fact]
    [Trait("UnitTest", "BedrockRuntime")]
    public void ExclusiveEnumeration_SecondClaimFailsUntilReleased()
    {
        int flag = 0;

        Assert.True(RealtimeAudioProtocol.TryBeginExclusiveEnumeration(ref flag));
        Assert.False(RealtimeAudioProtocol.TryBeginExclusiveEnumeration(ref flag));

        RealtimeAudioProtocol.EndExclusiveEnumeration(ref flag);

        Assert.True(RealtimeAudioProtocol.TryBeginExclusiveEnumeration(ref flag));
    }

    [Theory]
    [Trait("UnitTest", "BedrockRuntime")]
    [InlineData("user", "user")]
    [InlineData("ASSISTANT", "assistant")]
    [InlineData("System", "system")]
    [InlineData("tool", "tool")]
    public void MapRole_MapsKnownRolesCaseInsensitively(string input, string expected)
    {
        Assert.Equal(expected, RealtimeAudioProtocol.MapRole(input)?.Value);
    }

    [Theory]
    [Trait("UnitTest", "BedrockRuntime")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void MapRole_ReturnsNullForUnknownRoles(string? input)
    {
        Assert.Null(RealtimeAudioProtocol.MapRole(input));
    }
}
#endif

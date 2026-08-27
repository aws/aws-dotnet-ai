// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using Xunit;

namespace AWS.Speech.MEAI;

public class BargeInDetectorTests
{
    [Theory]
    [Trait("UnitTest", "Speech")]
    [InlineData("yes please stop")]
    [InlineData("wait")]
    [InlineData("  hey  ")]
    public void ShouldInterrupt_QualifyingPartial_ReturnsTrue(string partial)
    {
        Assert.True(BargeInDetector.ShouldInterrupt(partial));
    }

    [Theory]
    [Trait("UnitTest", "Speech")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hi")]
    public void ShouldInterrupt_NoiseOrTooShort_ReturnsFalse(string? partial)
    {
        Assert.False(BargeInDetector.ShouldInterrupt(partial));
    }
}
#endif

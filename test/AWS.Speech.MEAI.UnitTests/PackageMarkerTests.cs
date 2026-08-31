// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Xunit;

namespace AWS.Speech.MEAI;

public class PackageMarkerTests
{
    // Smoke test proving the test harness runs and InternalsVisibleTo reaches the package internals.
    // Real client and VoiceAgent tests land with their features in later PRs of the stack.
    [Fact]
    [Trait("UnitTest", "Speech")]
    public void PackageId_IsSet()
    {
        Assert.Equal("AWS.Speech.MEAI", PackageMarker.PackageId);
    }
}

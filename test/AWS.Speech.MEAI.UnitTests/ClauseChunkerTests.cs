// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using System.Text;
using Xunit;

namespace AWS.Speech.MEAI;

public class ClauseChunkerTests
{
    [Fact]
    [Trait("UnitTest", "Speech")]
    public void NextClauseBoundary_EmptyBuffer_ReturnsZero()
    {
        Assert.Equal(0, ClauseChunker.NextClauseBoundary(new StringBuilder()));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void NextClauseBoundary_ShortBoundaryBelowMinimum_ReturnsZero()
    {
        // Comma at index 5 (6 chars) is below MinClauseLength = 12; no flush yet.
        Assert.Equal(0, ClauseChunker.NextClauseBoundary(new StringBuilder("Hello,")));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void NextClauseBoundary_FirstQualifyingBoundary_FlushesThroughIt()
    {
        var buffer = new StringBuilder("Hello, world. How are you?");
        // '.' sits at index 12; MinClauseLength = 12, so first qualifying boundary is at position 12.
        Assert.Equal(13, ClauseChunker.NextClauseBoundary(buffer));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void NextClauseBoundary_NoPunctuation_ReturnsZero()
    {
        Assert.Equal(0, ClauseChunker.NextClauseBoundary(new StringBuilder("a much longer buffer with no punctuation")));
    }

    [Fact]
    [Trait("UnitTest", "Speech")]
    public void NextClauseBoundary_Newline_CountsAsBoundary()
    {
        // Newline at index 15 qualifies as a clause boundary.
        var buffer = new StringBuilder("first clause is\nnext clause");
        Assert.Equal(16, ClauseChunker.NextClauseBoundary(buffer));
    }
}
#endif

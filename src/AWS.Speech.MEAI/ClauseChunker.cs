// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
using System.Text;

namespace AWS.Speech.MEAI;

/// <summary>
/// Finds clause boundaries in streaming assistant text so the pipeline can send completed clauses to
/// text-to-speech while the model is still writing later ones. This keeps time-to-first-audio low.
/// </summary>
internal static class ClauseChunker
{
    /// <summary>Minimum clause length in characters; guards against flushing single-word fragments.</summary>
    internal const int MinClauseLength = 12;

    /// <summary>
    /// Returns the length of the prefix of <paramref name="buffer"/> that should be flushed as a clause,
    /// or 0 if no boundary meets the minimum length yet.
    /// </summary>
    /// <remarks>
    /// Walks from <see cref="MinClauseLength"/> - 1 onwards so short fragments never trigger a flush.
    /// Returns at the first boundary that qualifies so first-audio latency stays low.
    /// </remarks>
    public static int NextClauseBoundary(StringBuilder buffer)
    {
        for (int i = MinClauseLength - 1; i < buffer.Length; i++)
        {
            switch (buffer[i])
            {
                case '.':
                case '?':
                case '!':
                case ',':
                case ';':
                case ':':
                case '\n':
                    return i + 1;
            }
        }
        return 0;
    }
}
#endif

// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

#if NET8_0_OR_GREATER
namespace AWS.Speech.MEAI;

/// <summary>
/// Decides whether a speech-to-text partial arriving while the assistant is speaking is a real
/// interruption (barge-in) rather than noise. Kept as a pure predicate so the threshold is testable
/// without driving the whole loop.
/// </summary>
internal static class BargeInDetector
{
    /// <summary>Minimum trimmed partial length, in characters, that counts as a real interruption.</summary>
    internal const int MinPartialChars = 3;

    /// <summary>Returns <see langword="true"/> if <paramref name="partialText"/> clears the noise threshold.</summary>
    public static bool ShouldInterrupt(string? partialText) =>
        partialText is not null && partialText.Trim().Length >= MinPartialChars;
}
#endif

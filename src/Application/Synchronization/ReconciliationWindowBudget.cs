// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>Divides one reconciliation window between mail nobody has asked the server about and mail somebody has.</summary>
/// <remarks>
/// <para>
/// The division exists because the two groups compete and one of them is refilled by the run itself. A forward pass can
/// store more new occurrences than one window holds — the default batch settings admit ten batches of a hundred against
/// a window of five hundred — and every one of them arrives never-observed. Taking the window in observation order
/// alone would therefore spend all of it on mail that has just arrived, for as long as mail keeps arriving, and a
/// deletion or a flag change among the occurrences stored earlier would never be noticed again.
/// </para>
/// <para>
/// It lives here, apart from the query that applies it, because it is the rule rather than the retrieval: the split is
/// arithmetic that has to hold for every window size, while reading each group is a database concern.
/// </para>
/// </remarks>
public static class ReconciliationWindowBudget
{
    /// <summary>Decides how much of a window may go to occurrences nobody has asked the server about yet.</summary>
    /// <param name="maxEmailCount">The whole window, which the two groups together may not exceed.</param>
    /// <param name="previouslyObservedCandidateCount">How many already-observed occurrences are available to fill the reserve.</param>
    /// <returns>The greatest number of never-observed occurrences this window may take, never below zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxEmailCount" /> is not positive or <paramref name="previouslyObservedCandidateCount" /> is negative.</exception>
    /// <remarks>
    /// Half the window is reserved for previously observed occurrences, and only as much of that reserve as there are
    /// occurrences to fill it. A folder holding none therefore gives its whole window to new mail rather than leaving
    /// half of it idle, which is what makes the rule safe to apply to a mailbox being synchronized for the first time.
    /// The caller fills whatever the never-observed group leaves over from the reserved group, so a folder whose mail
    /// has all been observed likewise uses the whole window.
    /// </remarks>
    public static int NeverObservedShareOf(int maxEmailCount, int previouslyObservedCandidateCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);
        ArgumentOutOfRangeException.ThrowIfNegative(previouslyObservedCandidateCount);

        return maxEmailCount - Math.Min(maxEmailCount / 2, previouslyObservedCandidateCount);
    }
}

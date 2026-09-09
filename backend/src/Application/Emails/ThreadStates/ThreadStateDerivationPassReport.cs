// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>What one thread-state pass did, in counts and in the condition that stopped it.</summary>
/// <param name="DerivedThreadCount">How many conversations the pass settled a state for, including ones with nothing to say.</param>
/// <param name="StatedThreadCount">How many of those carried at least one statement.</param>
/// <param name="TooLargeThreadCount">How many were recorded as longer than one derivation may take in.</param>
/// <param name="StoppedBy">The condition that ended the pass early, or <see langword="null" /> where it ran to its own bound.</param>
/// <param name="ThreadsRemain">Whether the account still holds conversations awaiting a state.</param>
/// <remarks>
/// Counts and a reason, and nothing derived from a conversation: a statement is what the derivation wrote about
/// somebody's correspondence, so it never reaches a log, a span, or an instrument. The counts differ by exactly the
/// conversations a derivation found nothing to say about and the ones it declined to read, which are the two numbers an
/// operator reads to tell a derivation that is working from one that is answering emptily.
/// </remarks>
public readonly record struct ThreadStateDerivationPassReport(
    int DerivedThreadCount,
    int StatedThreadCount,
    int TooLargeThreadCount,
    ThreadStateWithholding? StoppedBy,
    bool ThreadsRemain)
{
    /// <summary>Gets whether the pass found nothing to do and nothing to report.</summary>
    /// <remarks>
    /// A deployment that has not turned the derivation on is in that state rather than stopped by something, so it is
    /// empty here although it names a withholding. The alternative would repeat one line per account per run for the
    /// life of every default deployment, saying each time that a feature nobody asked for is still off.
    /// </remarks>
    public bool IsEmpty =>
        this.DerivedThreadCount == 0
        && this.StoppedBy is null or ThreadStateWithholding.NotActivated;
}

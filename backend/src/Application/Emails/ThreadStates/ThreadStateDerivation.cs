// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>What one derivation produced: the statements it settled, or the reason it produced none this time.</summary>
/// <remarks>
/// The two outcomes are unequal on purpose, and the whole of what a caller does with one follows from which it holds.
/// A settled answer — including a settled answer of no statements at all — is written down and takes the conversation
/// out of the queue. A withheld one is not written down and stops the pass, because every reason a derivation is
/// withheld is a condition that outlives one conversation and would be met again by the next.
/// </remarks>
public sealed record ThreadStateDerivation
{
    private ThreadStateDerivation(
        ThreadStateCoverage coverage,
        IReadOnlyList<ThreadStateEntry> entries,
        ThreadStateWithholding? withheld)
    {
        this.Coverage = coverage;
        this.Entries = entries;
        this.Withheld = withheld;
    }

    /// <summary>Gets how much of the conversation the derivation was shown.</summary>
    public ThreadStateCoverage Coverage { get; }

    /// <summary>Gets what was derived, which is empty for a settled answer of nothing and for a withheld one alike.</summary>
    public IReadOnlyList<ThreadStateEntry> Entries { get; }

    /// <summary>Gets why nothing was derived, or <see langword="null" /> where the answer is settled.</summary>
    public ThreadStateWithholding? Withheld { get; }

    /// <summary>Gets whether the answer is one to write down.</summary>
    public bool IsSettled => this.Withheld is null;

    /// <summary>Records a derivation made over the whole conversation.</summary>
    /// <param name="entries">What was derived, and empty where there was nothing to say.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries" /> is <see langword="null" />.</exception>
    public static ThreadStateDerivation Settled(IReadOnlyList<ThreadStateEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new ThreadStateDerivation(ThreadStateCoverage.WholeThread, entries, withheld: null);
    }

    /// <summary>Records that the conversation is longer than one derivation may take in, so none was attempted.</summary>
    /// <returns>The derivation.</returns>
    /// <remarks>
    /// Settled rather than withheld, and empty rather than partial. It is settled because nothing about it changes on
    /// the next run; it is empty because a state derived from the part of a conversation that fits reads on a screen
    /// exactly like one derived from all of it, and a reader has no way to tell them apart.
    /// </remarks>
    public static ThreadStateDerivation TooLarge() =>
        new(ThreadStateCoverage.ThreadTooLarge, [], withheld: null);

    /// <summary>Records that nothing was derived, and why.</summary>
    /// <param name="withholding">The condition that stopped it.</param>
    /// <returns>The derivation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="withholding" /> is not a defined member.</exception>
    public static ThreadStateDerivation Withholding(ThreadStateWithholding withholding)
    {
        if (!Enum.IsDefined(withholding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(withholding),
                withholding,
                "A withheld derivation names one of the conditions this system stops on.");
        }

        return new ThreadStateDerivation(ThreadStateCoverage.WholeThread, [], withholding);
    }
}

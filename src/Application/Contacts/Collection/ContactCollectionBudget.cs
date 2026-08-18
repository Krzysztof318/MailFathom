// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Collection;

/// <summary>Bounds how many contacts one synchronization run may record.</summary>
/// <remarks>
/// <para>
/// The bound belongs to a run rather than to the book, for the reason the content budget beside it does: what needs
/// pacing is the first synchronization of a mailbox holding years of mail, where every message is new and a book could
/// otherwise gain thousands of people in one pass before anybody had seen one of them. A run that reaches the bound
/// stops recording and leaves the rest for the next run, which reads the same mail's senders again and finds the count
/// they need still standing.
/// </para>
/// <para>
/// The claim is interlocked because an account may synchronize several folders at once and the bound is meant to be the
/// run's rather than each folder's share of it.
/// </para>
/// </remarks>
public sealed class ContactCollectionBudget
{
    private readonly int ceiling;
    private int claimed;

    /// <summary>Opens a budget for one synchronization run.</summary>
    /// <param name="ceiling">How many contacts the run may record; zero records none.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="ceiling" /> is negative.</exception>
    public ContactCollectionBudget(int ceiling)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ceiling);

        this.ceiling = ceiling;
    }

    /// <summary>Gets how many contacts this run has recorded.</summary>
    public int Recorded => Volatile.Read(ref this.claimed);

    /// <summary>Claims room for one contact this run is about to record.</summary>
    /// <returns><see langword="true" /> when the run may still record one; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Claimed immediately before the write rather than per address considered, so an excluded address and one already
    /// in the book cost the run nothing. A refused claim leaves the count where it was, so <see cref="Recorded" />
    /// answers with what was written however often the bound was asked past.
    /// </remarks>
    public bool TryClaim()
    {
        var observed = Volatile.Read(ref this.claimed);

        while (observed < this.ceiling)
        {
            var witnessed = Interlocked.CompareExchange(ref this.claimed, observed + 1, observed);

            if (witnessed == observed)
            {
                return true;
            }

            observed = witnessed;
        }

        return false;
    }
}

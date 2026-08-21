// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs;

/// <summary>Holds one job for one attempt, until an instant rather than until somebody lets go.</summary>
/// <remarks>
/// <para>
/// A lease is a stamped row rather than a flag, which is what makes a crash recoverable without anything being told a
/// process died: an expired lease is claimable again, so work in flight when a process stops is picked up on its own.
/// The claiming transaction ends with the claim, because the work itself reaches a mail server or a model provider and
/// no transaction may stay open across one.
/// </para>
/// <para>
/// Two things keep that safe rather than merely likely. Every write against a leased job is conditional on the owner
/// still matching, so a late writer whose lease was reclaimed writes nothing. And the timeout an attempt runs under is
/// strictly shorter than the lease it holds, so an attempt is cancelled before its lease can expire underneath it —
/// which is what makes two workers running one job structurally impossible rather than rare.
/// </para>
/// </remarks>
/// <param name="Owner">The attempt the lease is held by.</param>
/// <param name="ExpiresAt">The instant after which the job is claimable again whatever the holder is doing.</param>
public sealed record JobLease(JobLeaseOwner Owner, DateTimeOffset ExpiresAt)
{
    /// <summary>Reports whether the lease has run out by a given instant.</summary>
    /// <param name="instant">The instant to judge the lease at.</param>
    /// <returns><see langword="true" /> when the lease no longer holds the job.</returns>
    /// <remarks>
    /// The expiry instant itself counts as expired, matching the claim statement's own comparison. A lease is judged
    /// against the database's clock where it decides a claim; this answers the same question for a caller that has
    /// already read the row.
    /// </remarks>
    public bool HasExpiredAt(DateTimeOffset instant) => this.ExpiresAt <= instant;

    /// <summary>Reports whether a given attempt is the one holding this lease.</summary>
    /// <param name="owner">The attempt asking.</param>
    /// <returns><see langword="true" /> when the lease is held by that attempt.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner" /> is <see langword="null" />.</exception>
    public bool IsHeldBy(JobLeaseOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return this.Owner == owner;
    }
}

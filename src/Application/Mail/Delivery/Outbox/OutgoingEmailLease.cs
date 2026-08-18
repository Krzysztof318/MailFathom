// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Holds one queued send for one attempt, until an instant rather than until somebody lets go.</summary>
/// <remarks>
/// <para>
/// A lease is a stamped row rather than a flag, which is what makes a crash recoverable without anything being told a
/// process died: an expired lease is claimable again, so a send in flight when a process stops is picked up on its own.
/// The claiming transaction ends with the claim, because the attempt itself reaches a submission server and no
/// transaction may stay open across one.
/// </para>
/// <para>
/// Two things keep that safe rather than merely likely, and they are the same two the durable job queue rests on. Every
/// write against a leased record is conditional on the owner still matching, so a late writer whose lease was reclaimed
/// writes nothing. And an attempt runs under a timeout strictly shorter than the lease it holds, so it is cancelled
/// before its lease can expire underneath it.
/// </para>
/// <para>
/// What a lease never does here is release a send whose transmission had begun. The claim reaches records that have
/// issued no SMTP command at all, so a message whose body may already have gone out is not handed to a second attempt
/// by an expiry — the expiry is what makes work recoverable, and that one record is precisely the work that is not.
/// </para>
/// <para>
/// The owner is a generated identity for the attempt rather than for the process, because two replicas of one
/// deployment are the case the compare-and-set exists for and neither can see what the other allocated. It is not a
/// security token, so the ordinary UUID generator is what it needs.
/// </para>
/// </remarks>
/// <param name="Owner">The attempt the lease is held by.</param>
/// <param name="ExpiresAt">The instant after which the record is claimable again whatever the holder is doing.</param>
public sealed record OutgoingEmailLease(Guid Owner, DateTimeOffset ExpiresAt)
{
    /// <summary>Reports whether the lease has run out by a given instant.</summary>
    /// <param name="instant">The instant to judge the lease at.</param>
    /// <returns><see langword="true" /> when the lease no longer holds the record.</returns>
    /// <remarks>
    /// The expiry instant itself counts as expired, matching the claim statement's own comparison. A lease is judged
    /// against the database's clock where it decides a claim; this answers the same question for a caller that has
    /// already read the row.
    /// </remarks>
    public bool HasExpiredAt(DateTimeOffset instant) => this.ExpiresAt <= instant;
}

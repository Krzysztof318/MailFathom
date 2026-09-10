// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Holds one scope for one holder, until an instant rather than until somebody lets go.</summary>
/// <remarks>
/// <para>
/// A lease is a stamped row rather than a flag, which is what makes a lost replica recoverable without anything being
/// told it died: an expired lease is takeable, so a scope a crashed process was holding is taken again on its own. The
/// claiming transaction ends with the claim, because the work itself reaches a mail server or a model provider and no
/// transaction may stay open across one.
/// </para>
/// <para>
/// What it promises is <strong>one writer, never one runner</strong>. Every write against a leased scope is conditional
/// on the holder still matching, so a replica whose lease was reclaimed commits nothing; what is not excluded is that
/// it is still running, and what bounds that is the holder cancelling its own work on the first renewal it fails to
/// complete rather than when the expiry it last read has passed. A failed renewal happens strictly before the expiry,
/// and the difference is the margin that leaves the cancellation time to reach a session rather than only a loop.
/// </para>
/// </remarks>
/// <param name="Scope">The unit of work the lease holds.</param>
/// <param name="Holder">The hold the lease is held under.</param>
/// <param name="ExpiresAt">The instant after which the scope is takeable again whatever the holder is doing.</param>
public sealed record WorkLease(WorkScope Scope, WorkLeaseHolder Holder, DateTimeOffset ExpiresAt)
{
    /// <summary>Reports whether the lease has run out by a given instant.</summary>
    /// <param name="instant">The instant to judge the lease at.</param>
    /// <returns><see langword="true" /> when the lease no longer holds the scope.</returns>
    /// <remarks>
    /// The expiry instant itself counts as expired, matching the claim statement's own comparison. A lease is judged
    /// against the database's clock where it decides a claim; this answers the same question for a caller that already
    /// holds one.
    /// </remarks>
    public bool HasExpiredAt(DateTimeOffset instant) => this.ExpiresAt <= instant;

    /// <summary>Reports whether a given hold is the one holding this lease.</summary>
    /// <param name="holder">The hold asking.</param>
    /// <returns><see langword="true" /> when the lease is held under that hold.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="holder" /> is <see langword="null" />.</exception>
    public bool IsHeldBy(WorkLeaseHolder holder)
    {
        ArgumentNullException.ThrowIfNull(holder);

        return this.Holder == holder;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Coordination;

/// <summary>Hands exclusive hold of a named unit of work to one holder at a time, for as long as it keeps renewing.</summary>
/// <remarks>
/// <para>
/// One mechanism for every kind of work that must not run twice — a mailbox supervisor, a deployment-wide sweep, an
/// operator's re-derivation, the stored-content move — so that none of them grows an idea of its own about what holding
/// work means. Which scope one unit of that work is, is the key its own progress row already carries.
/// </para>
/// <para>
/// The exclusion is the database's rather than this contract's. A claim is one statement that inserts the scope or
/// takes over an expired lease, so two replicas asking for one scope at the same instant leave one holder because
/// PostgreSQL refused the other, not because either checked first; and renewal and release are each a single
/// conditional update that writes nothing once the lease has moved on.
/// </para>
/// <para>
/// <strong>No method here takes a persistence session</strong>, and that is the contract rather than an omission. A
/// hold outlives the transaction that took it, because the work it guards reaches a mail server or a model provider and
/// no transaction may stay open across one — so there is nothing for a caller to enlist a claim in, and a hold cannot
/// be lost by a rollback somewhere above it.
/// </para>
/// <para>
/// What a holder is promised is <strong>at most one writer, and never at most one runner</strong>, which
/// <see cref="WorkLease" /> states in full. A holder that fails to renew stops its own work rather than waiting for the
/// expiry, and a scope's exclusion reaches MailFathom's own state rather than a connection a mail server has already
/// accepted.
/// </para>
/// </remarks>
public interface IWorkLeaseStore
{
    /// <summary>Takes exclusive hold of a scope nothing holds, or whose holder's lease has run out.</summary>
    /// <param name="scope">The unit of work to hold.</param>
    /// <param name="holder">The hold the lease would be stamped with.</param>
    /// <param name="leaseDuration">How long the scope is held from now, unless it is renewed.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The lease this call took, or <see langword="null" /> when the scope is held by an unexpired lease.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="holder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leaseDuration" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// An absent answer is not a failure and not something to report: the work is somebody else's for now, so a caller
    /// waits and asks again on the interval it would have run on. What holds what is a question the lease table answers
    /// about the deployment rather than one the replica that happened to ask answers about itself.
    /// </para>
    /// <para>
    /// A holder that already holds the scope is refused as well, because a live lease is a live lease whoever asks. A
    /// holder extends what it has through <see cref="RenewAsync" />, which is the one operation that says so.
    /// </para>
    /// </remarks>
    Task<WorkLease?> ClaimAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Pushes a held lease further out, so a long-running hold is not taken from underneath it.</summary>
    /// <param name="scope">The scope whose lease is renewed.</param>
    /// <param name="holder">The hold claiming to hold it.</param>
    /// <param name="leaseDuration">How much longer the scope is held from now.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The renewed lease, or <see langword="null" /> when this hold no longer holds the scope.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="holder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leaseDuration" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// An absent answer is the signal to stop working, and to stop at once rather than at the next pass: the scope
    /// belongs to another hold, so anything this one goes on to commit would be a second writer's.
    /// </para>
    /// <para>
    /// The condition is the holder rather than the expiry, so a hold whose lease has run out but which nothing has
    /// taken renews it and goes on working. That is safe because nothing else holds the scope, and refusing on the
    /// expiry instead would abandon work nobody else had taken.
    /// </para>
    /// </remarks>
    Task<WorkLease?> RenewAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>Gives a held scope back at once, so another replica can take it without waiting out the expiry.</summary>
    /// <param name="scope">The scope to release.</param>
    /// <param name="holder">The hold claiming to hold it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when this hold still held the scope and the release was written; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="holder" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// What a graceful shutdown does with what it was holding, and an optimization of the ordinary case only: the
    /// expiry is what covers a crash, and nothing may be built on a release having happened. Writing nothing is the
    /// point of the compare-and-set — a late release from a hold that was already reclaimed would otherwise free a
    /// scope another replica is working under.
    /// </remarks>
    Task<bool> ReleaseAsync(WorkScope scope, WorkLeaseHolder holder, CancellationToken cancellationToken);
}

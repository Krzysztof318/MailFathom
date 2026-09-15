// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Takes one user off this deployment, with everything it recorded for them.</summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IMailUserProvisioning" /> and deliberately a port of its own, because the two are
/// not one capability with a flag. Provisioning runs on every start against a roster that ordinarily has not changed
/// and is idempotent for that reason; this runs when a person asked for it, once, and what it removes cannot be
/// written back. A deployment that never calls it goes on holding a user it no longer serves, which is the state the
/// startup gate reports rather than repairs.
/// </para>
/// <para>
/// It is the whole of the erasure rather than the user row alone. The mail graph hangs on that row through
/// <c>mailbox_accounts.UserId</c>, so the accounts, the folders, the mail beneath them, everything derived from that
/// mail, and the contact book this user assembled all go with it — and the rows that name a mail account without
/// keying onto one are taken by the adapter, since no constraint would have reached them.
/// </para>
/// </remarks>
public interface IMailUserErasure
{
    /// <summary>Erases one user and everything this deployment recorded for them.</summary>
    /// <param name="user">The user to remove.</param>
    /// <param name="quiescedAccounts">
    /// The mail accounts the caller has stopped every writer against and is holding stopped for the whole call. An
    /// account the transaction finds itself about to delete and which is not among these refuses the erasure.
    /// </param>
    /// <param name="cancellationToken">Cancels the erasure before it commits, leaving the deployment unchanged.</param>
    /// <returns>What the attempt removed, or the account that stopped it removing anything.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="quiescedAccounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The whole of it commits or none of it does, because a partial erasure is a data-subject request answered with
    /// half of somebody's mail still stored. An erasure that removed nothing and was not refused is the repeat of one
    /// that already ran rather than a failure: what the caller asked for is that the deployment hold nothing for this
    /// user, and it does.
    /// </para>
    /// <para>
    /// The accounts are handed in rather than discovered here because stopping a writer is the caller's act and takes
    /// a lease no adapter of this port owns. What this port then owes is the other half: the set it is about to delete
    /// is recomputed inside the transaction, so an account that became solely this user's after the caller read the
    /// roster refuses rather than being deleted with nothing holding it still.
    /// </para>
    /// </remarks>
    Task<MailUserErasureOutcome> EraseAsync(
        MailUserId user,
        IReadOnlyList<Guid> quiescedAccounts,
        CancellationToken cancellationToken);
}

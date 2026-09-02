// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Takes one owner off this deployment, with everything it recorded for them.</summary>
/// <remarks>
/// <para>
/// The counterpart of <see cref="IMailOwnerProvisioning" /> and deliberately a port of its own, because the two are
/// not one capability with a flag. Provisioning runs on every start against a roster that ordinarily has not changed
/// and is idempotent for that reason; this runs when a person asked for it, once, and what it removes cannot be
/// written back. A deployment that never calls it goes on holding an owner it no longer serves, which is the state the
/// startup gate reports rather than repairs.
/// </para>
/// <para>
/// It is the whole of the erasure rather than the owner row alone. The mail graph hangs on that row through
/// <c>mailbox_accounts.OwnerId</c>, so the accounts, the folders, the mail beneath them, everything derived from that
/// mail, and the contact book this owner assembled all go with it — and the rows that name a mail account without
/// keying onto one are taken by the adapter, since no constraint would have reached them.
/// </para>
/// </remarks>
public interface IMailOwnerErasure
{
    /// <summary>Erases one owner and everything this deployment recorded for them.</summary>
    /// <param name="owner">The owner to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits, leaving the deployment unchanged.</param>
    /// <returns><see langword="true" /> when an owner record was there to remove, <see langword="false" /> when the deployment held none.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The whole of it commits or none of it does, because a partial erasure is a data-subject request answered with
    /// half of somebody's mail still stored. False is the repeat of an erasure that already ran rather than a failure:
    /// what the caller asked for is that the deployment hold nothing for this owner, and it does.
    /// </remarks>
    Task<bool> EraseAsync(MailOwnerId owner, CancellationToken cancellationToken);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Access;

/// <summary>Places what an administrative operation names in the deployment, so a scope can be asked whether it covers it.</summary>
/// <remarks>
/// <para>
/// It answers from the records a deployment holds at the moment it is asked, never from a copy a replica keeps, because
/// moving a user or a mailbox between organizations has to reach the very next check on every replica: a placement read
/// from a cache would let an administrator of the organization somebody just left go on reading their mail until it
/// expired.
/// </para>
/// <para>
/// Something the deployment does not hold is answered <see cref="AdministrativeTarget.Unplaced" /> rather than refused.
/// What decides whether an unknown target is an error is the operation; what decides who reaches it is this, and
/// nothing narrower than the deployment reaches a target nobody can place.
/// </para>
/// </remarks>
public interface IAdministrativeTargets
{
    /// <summary>Places mail accounts in one read: the organization each belongs to and whether one user alone is assigned it.</summary>
    /// <param name="accounts">The accounts, as many as one operation names — one, or the collected books of one user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Where each account sits, keyed by every account asked about, with <see cref="AdministrativeTarget.Unplaced" /> for one the deployment does not hold.</returns>
    Task<IReadOnlyDictionary<MailAccountId, AdministrativeTarget>> PlaceMailAccountsAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken);

    /// <summary>Places one user: the organization they are a member of.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Where the user sits, or <see cref="AdministrativeTarget.Unplaced" /> for one the deployment holds no record for.</returns>
    Task<AdministrativeTarget> PlaceUserAsync(UserId user, CancellationToken cancellationToken);
}

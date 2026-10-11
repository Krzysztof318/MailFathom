// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;

/// <summary>Finds the users whose endpoint switches were written past the settings policy, and derives them again.</summary>
/// <remarks>
/// <para>
/// A user's row carries the two switches a request is admitted by, and a stamp saying which version of the record and
/// which organization they were derived from. Every write of this build that derives them writes the stamp with them.
/// A build that does not know settings policies writes neither half of the stamp, so the record it wrote, or the user
/// it moved, is a row whose stamp no longer matches it: that is the whole of what tells such a row apart, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0037-a-settings-policy-at-the-deployment-and-at-each-organization.md">ADR 0037</see>
/// § <em>How a value resolves</em> is where it is decided.
/// </para>
/// <para>
/// The two operations are apart because only the second is a write. Which rows trail is asked outside any
/// transaction and is allowed to be out of date; what a row is derived from is read again inside the write, so
/// nothing the first answer said is relied on beyond which user to look at.
/// </para>
/// </remarks>
public interface IUserEndpointSwitchStore
{
    /// <summary>Reads the users whose stamp does not match their row, in identifier order.</summary>
    /// <param name="after">The user the previous reading ended on, to read on from, or <see langword="null" /> to read from the first.</param>
    /// <param name="limit">The greatest number of users to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>No more than <paramref name="limit" /> such users, each past <paramref name="after" />; no document travels with them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the database declined the read.</exception>
    Task<IReadOnlyList<UserId>> ReadTrailingAsync(Guid? after, int limit, CancellationToken cancellationToken);

    /// <summary>Derives one user's endpoint switches again and stamps their row, if its stamp still does not match it.</summary>
    /// <param name="user">The user.</param>
    /// <param name="derive">
    /// Derives the switches the row is to carry from the record as the row holds it, or answers
    /// <see langword="null" /> to leave the row exactly as it stands, stamp included. It is called at most once,
    /// inside the write's transaction, and performs no I/O of its own. Nothing is changing which policies govern the
    /// record, so the ones it stands under and the ones it would stand under are the same policies.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the row was written. It is not where the deployment holds no such user, where the row's stamp matches by the time it is locked — another replica derived it first — or where <paramref name="derive" /> answered nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="derive" /> is <see langword="null" />.</exception>
    /// <exception cref="UserSettingsUnwritableException">Thrown when the write did not commit.</exception>
    /// <remarks>
    /// The record, the switches the row holds, and the policies governing it are read inside the write's own
    /// transaction, with the row locked and under the lock a policy write excludes record writes with. So a policy
    /// write either commits first, and the switches are derived from it, or commits second, and rewrites the row
    /// itself: the row is never left holding switches derived from a policy that has since been replaced, stamped as
    /// current. The record's version is not moved, because the record did not change.
    /// </remarks>
    Task<bool> DeriveAgainAsync(
        UserId user,
        Func<GovernedUserRecord, UserEndpointAccess?> derive,
        CancellationToken cancellationToken);
}

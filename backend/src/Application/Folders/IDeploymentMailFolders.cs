// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Answers which folders of every account this deployment serves take part in one thing, as a set a query can be narrowed by.</summary>
/// <remarks>
/// <para>
/// The set-based shape of the decisions <see cref="IMailFolderParticipationReader" /> and
/// <see cref="IJunkMailFolderCatalog" /> answer per folder. A walk over stored mail narrows a table and cannot ask one
/// folder at a time, so it reads the whole set once, before it starts, and narrows by that value for the rest of its
/// work. Asking again per message would put a database round trip in front of every row.
/// </para>
/// <para>
/// Every set is read from the account records themselves rather than from what any one process holds in memory, so the
/// number of accounts a deployment serves is a question of how large the answer is rather than of what every replica
/// must keep. The per-folder answers read the settings their scope was prepared with until #2330 moves them, and those
/// were read from the account records when the scope was prepared, so the two agree for every scope prepared after the
/// account's last write committed, on every replica alike.
/// </para>
/// <para>
/// Every set names what is admitted rather than what is withheld, because a set of names cannot exclude a folder nobody
/// named: an account's settings enumerate the folders MailFathom has, so anything outside them is a folder this
/// deployment does not have.
/// </para>
/// </remarks>
public interface IDeploymentMailFolders
{
    /// <summary>Reads the folders of every served account that take part in one thing.</summary>
    /// <param name="selection">What the folders take part in.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The folders, ordered by account and alias; empty when no served account has one.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="selection" /> is not a defined member.</exception>
    Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(MailFolderSelection selection, CancellationToken cancellationToken);

    /// <summary>Reads the folders of the named accounts that take part in one thing.</summary>
    /// <param name="selection">What the folders take part in.</param>
    /// <param name="accounts">The accounts asked about; one this deployment does not serve contributes nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The folders, ordered by account and alias.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="selection" /> is not a defined member.</exception>
    /// <remarks>The shape a read acting for one user takes, which narrows by the accounts that user is assigned and has no use for anybody else's folders.</remarks>
    Task<IReadOnlyList<MailFolderIdentity>> ReadAsync(
        MailFolderSelection selection,
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken);
}

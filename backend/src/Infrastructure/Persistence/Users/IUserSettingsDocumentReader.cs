// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads one user's persisted record.</summary>
/// <remarks>
/// One read answers for one user and never for the deployment. That is the whole of the contract's shape: a
/// user-scoped view is a view of one person's record, so the read is a key lookup rather than a query, and a caller
/// holding several users asks for each rather than being handed a page of other people's documents to filter. The one
/// deployment-wide read is of versions alone, which carries no document and is what tells a replica whose record to ask
/// for.
/// </remarks>
public interface IUserSettingsDocumentReader
{
    /// <summary>Reads the version every user's record stands at, without any record.</summary>
    /// <param name="limit">The greatest number of versions to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each held user and the version of their record, in the order the users were recorded in, and no more than <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the database declined the read.</exception>
    /// <remarks>What a replica compares its roster against on every interval, so it is one statement and sends no document: a record is read only once its version says it moved.</remarks>
    Task<IReadOnlyList<UserSettingsDocumentVersion>> ReadVersionsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Reads the record of one user.</summary>
    /// <param name="user">The user whose record is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The user's record, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the deployment holds a record for this user and it could not be handed on — a document past what this build binds, or a database that declined the read. A user nobody provisioned is the <see langword="null" /> above rather than this.</exception>
    Task<UserSettingsDocument?> ReadAsync(MailUserId user, CancellationToken cancellationToken);
}

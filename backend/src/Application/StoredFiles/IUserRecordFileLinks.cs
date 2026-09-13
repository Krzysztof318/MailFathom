// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;

namespace MailFathom.Application.StoredFiles;

/// <summary>Reads and writes the links a user's record holds to their stored files.</summary>
/// <remarks>
/// A link is a reference in the record's document, so writing one is a write of that document: it is judged by the
/// rules every write of the record is judged by, including that a link naming a file the user does not own is refused,
/// and it is committed over the version it was composed against.
/// </remarks>
public interface IUserRecordFileLinks
{
    /// <summary>Reads every file one user's record links to.</summary>
    /// <param name="user">The user whose record is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The linked files, empty when the record links none or this deployment holds no such user.</returns>
    Task<IReadOnlySet<StoredFileId>> ReadLinkedFilesAsync(MailUserId user, CancellationToken cancellationToken);

    /// <summary>Reads the file one user's record links as their portrait.</summary>
    /// <param name="user">The user whose record is read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The portrait's file, or <see langword="null" /> when the record links none or this deployment holds no such user.</returns>
    Task<StoredFileId?> FindPortraitAsync(MailUserId user, CancellationToken cancellationToken);

    /// <summary>Links the signed-in user's record to a portrait, or to none.</summary>
    /// <param name="portrait">The file to link, or <see langword="null" /> to link none.</param>
    /// <param name="cancellationToken">Cancels the read and the commit.</param>
    /// <returns>Whether the user is held, and the file the record linked before this write.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the record refused the link for a reason other than a concurrent write.</exception>
    /// <remarks>
    /// It names no user, and resolves the one it writes for from the caller itself, so no implementation can be handed
    /// somebody else's record to rewrite.
    /// </remarks>
    Task<PortraitRelinking> RelinkOwnPortraitAsync(StoredFileId? portrait, CancellationToken cancellationToken);
}

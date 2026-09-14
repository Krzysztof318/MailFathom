// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Local;

/// <summary>Reads and writes the stored state a change on a held account commits to directly.</summary>
/// <remarks>
/// Every member joins the session it is given, because a local change and its audit entry are one transaction. A read
/// is scoped to the account it names, so a message of another account answers as absent rather than as found.
/// </remarks>
public interface ILocalEmailStateStore
{
    /// <summary>Reads where a stored message is and the flags and keywords it carries.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state, or <see langword="null" /> where the account holds no such message.</returns>
    Task<LocalEmailState?> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken);

    /// <summary>Writes the local folder, the flags, and the keywords a change left the message with.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="email">The message.</param>
    /// <param name="state">The state to write; its source folder is not written, since no local change moves an occurrence.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the write is staged in the session.</returns>
    Task WriteAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        LocalEmailState state,
        CancellationToken cancellationToken);

    /// <summary>Erases the message, its payload, and everything derived from it.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="account">The account the message is stored for.</param>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>A task that completes once the erasure is staged in the session.</returns>
    Task EraseAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken);
}

/// <summary>Where a stored message is kept and what it carries, as a local change reads and writes it.</summary>
/// <param name="SourceFolder">The binding the message's occurrence names, which is what a listing and a signal name it by.</param>
/// <param name="Folder">The local folder the message is in, or <see langword="null" /> where none has been assigned yet.</param>
/// <param name="IsSeen">Whether the message is read.</param>
/// <param name="IsFlagged">Whether the message is starred.</param>
/// <param name="Keywords">The keywords the message carries.</param>
public sealed record LocalEmailState(
    MailFolderResolution SourceFolder,
    LocalMailFolderId? Folder,
    bool IsSeen,
    bool IsFlagged,
    RemoteEmailKeywords Keywords);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Domain.Emails;

/// <summary>Identifies one stable remote IMAP email occurrence.</summary>
/// <remarks>
/// The identity deliberately uses account, folder, UIDVALIDITY, and UID because IMAP UIDs are stable only within one
/// folder UIDVALIDITY scope. The folder component is a <see cref="MailFolderResolutionId" /> rather than an alias,
/// because an alias can be repointed to a different remote folder and UIDVALIDITY does not distinguish the two.
/// </remarks>
public sealed record EmailOccurrenceId(
    MailAccountId AccountId,
    MailFolderResolutionId FolderResolutionId,
    ImapUidValidity UidValidity,
    ImapUid Uid)
{
    /// <summary>Creates a stable remote occurrence identity.</summary>
    /// <param name="accountId">The owning local account.</param>
    /// <param name="folderResolutionId">The alias binding the occurrence was read under.</param>
    /// <param name="uidValidity">The folder UIDVALIDITY value.</param>
    /// <param name="uid">The email UID.</param>
    /// <returns>A stable email occurrence identity.</returns>
    public static EmailOccurrenceId Create(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        ImapUid uid) => new(accountId, folderResolutionId, uidValidity, uid);
}

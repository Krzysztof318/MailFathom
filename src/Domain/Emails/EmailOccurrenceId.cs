// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Domain.Emails;

/// <summary>Identifies one stable remote IMAP email occurrence.</summary>
/// <remarks>The identity deliberately uses account, folder, UIDVALIDITY, and UID because IMAP UIDs are stable only within one folder UIDVALIDITY scope.</remarks>
public sealed record EmailOccurrenceId(MailAccountId AccountId, MailFolderName FolderName, ImapUidValidity UidValidity, ImapUid Uid)
{
    /// <summary>Creates a stable remote occurrence identity.</summary>
    /// <param name="accountId">The owning local account.</param>
    /// <param name="folderName">The remote folder name.</param>
    /// <param name="uidValidity">The folder UIDVALIDITY value.</param>
    /// <param name="uid">The email UID.</param>
    /// <returns>A stable email occurrence identity.</returns>
    public static EmailOccurrenceId Create(MailAccountId accountId, MailFolderName folderName, ImapUidValidity uidValidity, ImapUid uid) => new(accountId, folderName, uidValidity, uid);
}

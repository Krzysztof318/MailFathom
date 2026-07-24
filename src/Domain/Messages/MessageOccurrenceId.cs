// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Domain.Messages;

/// <summary>Identifies one stable remote IMAP message occurrence.</summary>
/// <remarks>The identity deliberately uses account, folder, UIDVALIDITY, and UID because IMAP UIDs are stable only within one folder UIDVALIDITY scope.</remarks>
public sealed record MessageOccurrenceId(MailAccountId AccountId, MailFolderName FolderName, ImapUidValidity UidValidity, ImapUid Uid)
{
    /// <summary>Creates a stable remote occurrence identity.</summary>
    /// <param name="accountId">The owning local account.</param>
    /// <param name="folderName">The remote folder name.</param>
    /// <param name="uidValidity">The folder UIDVALIDITY value.</param>
    /// <param name="uid">The message UID.</param>
    /// <returns>A stable message occurrence identity.</returns>
    public static MessageOccurrenceId Create(MailAccountId accountId, MailFolderName folderName, ImapUidValidity uidValidity, ImapUid uid) => new(accountId, folderName, uidValidity, uid);
}

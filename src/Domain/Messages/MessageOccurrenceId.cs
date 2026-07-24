// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Domain.Messages;

/// <summary>Identifies one stable remote IMAP message occurrence.</summary>
/// <remarks>
/// The identity deliberately uses account, folder, UIDVALIDITY, and UID because IMAP UIDs are stable only within one folder UIDVALIDITY scope.
/// Every component is already a validated value object, so the primary constructor is the single construction path and no separate factory is offered.
/// </remarks>
/// <param name="AccountId">The owning local account.</param>
/// <param name="FolderName">The remote folder name.</param>
/// <param name="UidValidity">The folder UIDVALIDITY value.</param>
/// <param name="Uid">The message UID.</param>
public sealed record MessageOccurrenceId(MailAccountId AccountId, MailFolderName FolderName, ImapUidValidity UidValidity, ImapUid Uid);

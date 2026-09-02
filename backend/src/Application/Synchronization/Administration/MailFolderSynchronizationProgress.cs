// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>How far one folder's durable synchronization progress has come, and when it last moved.</summary>
/// <remarks>
/// <para>
/// This is the half of a folder's status that survives a restart, and the half that separates a folder with nothing left
/// to fetch from one that has been repeating the same batch. A run that fails before committing leaves the instant where
/// it was, so an alias whose progress last moved a day ago while its runs keep ending is stuck rather than quiet.
/// </para>
/// <para>
/// An alias can have been bound to several remote folders over time and each binding keeps progress of its own. What is
/// reported is the binding that moved most recently, because the older ones describe a UID space the mail server has
/// already renumbered away from.
/// </para>
/// </remarks>
/// <param name="Folder">The account and alias the progress belongs to.</param>
/// <param name="UidValidity">The UID space the progress was made in, which changes when a mail server recreates the folder.</param>
/// <param name="LastSeenUid">The newest UID the forward pass has durably processed, or <see langword="null" /> when the space is empty.</param>
/// <param name="AdvancedAt">When the progress last moved.</param>
public sealed record MailFolderSynchronizationProgress(
    MailFolderIdentity Folder,
    ImapUidValidity UidValidity,
    ImapUid? LastSeenUid,
    DateTimeOffset AdvancedAt);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>What local state holds about one folder: where the mail server keeps it, and how much of it is stored here.</summary>
/// <param name="Folder">The account and alias the entry belongs to.</param>
/// <param name="RemotePath">The remote folder the alias is currently bound to, which is where the folder's place in the mailbox hierarchy comes from.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds and would serve.</param>
/// <param name="UnreadEmailCount">How many of those the mail server last reported without <c>\Seen</c>.</param>
/// <remarks>
/// <para>
/// The path is the newest binding's, because an alias rebound after a server recreated the folder names the folder it
/// names now. The counts are of every binding's mail together, because the older bindings' emails are still listed
/// under the same alias and no reader counting a folder is asking which generation produced them.
/// </para>
/// <para>
/// Both counts are of the local copy rather than of the mailbox. A folder the deployment is still backfilling holds
/// fewer than the server does, which is why nothing reading this may present it as the mailbox's own figure without the
/// folder's freshness beside it.
/// </para>
/// <para>
/// The unread count is read from the flag snapshot reconciliation writes and is never written back towards the server:
/// MailFathom reads mail read-only, and counting what is unread sets nothing.
/// </para>
/// </remarks>
public sealed record StoredMailFolder(
    MailFolderIdentity Folder,
    RemoteFolderPath RemotePath,
    int StoredEmailCount,
    int UnreadEmailCount);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Answers how far into MailFathom each configured folder is admitted.</summary>
/// <remarks>
/// <para>
/// The decision is configuration's and is read through a port for the reason every other per-account decision is: the
/// paths that have to obey it are synchronization, chunking, and every mailbox read, and none of them may reach for a
/// settings type of its own. Reading it in one place is also what keeps a folder hidden from every tool at once rather
/// than from the tools somebody remembered.
/// </para>
/// <para>
/// The three lists exist beside <see cref="GetParticipation" /> because a query cannot ask one folder at a time. A read
/// narrows a table and needs the whole admitted set as a value it can put into a predicate, while a write path holds
/// one email and asks about that email's folder; both answers come from the same configuration, so neither can drift
/// from the other.
/// </para>
/// <para>
/// Every list names what is admitted rather than what is withheld, because a set of names cannot exclude a folder nobody
/// named. Configuration enumerates the folders MailFathom has, so anything outside it is not a folder that was left in
/// by an exclusion that failed to mention it — it is a folder this deployment does not have.
/// </para>
/// </remarks>
public interface IMailFolderParticipationReader
{
    /// <summary>Gets the folders whose mail this deployment mirrors, and no others.</summary>
    /// <remarks>
    /// It is what a pass over stored mail runs against — rule evaluation today — rather than what a synchronization run
    /// schedules, which reads the same decision one account at a time. Switching a folder's synchronization off keeps
    /// what it had already stored, so such a walk meets rows nothing refreshes and rows of folders configuration no
    /// longer names at all; admitting the mirrored folders is what leaves both out, and it is one list rather than two
    /// because a walk asking which folders it may act on is asking one question.
    /// </remarks>
    IReadOnlyList<MailFolderIdentity> FoldersSynchronized { get; }

    /// <summary>Gets the folders an MCP tool may list, search, read, or answer from, and no others.</summary>
    IReadOnlyList<MailFolderIdentity> FoldersVisibleToTools { get; }

    /// <summary>Gets the folders whose content is cut into passages and embedded, and no others.</summary>
    IReadOnlyList<MailFolderIdentity> FoldersGeneratingEmbeddings { get; }

    /// <summary>Gets what one folder takes part in.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns>
    /// The configured participation, or <see cref="MailFolderParticipation.Unmapped" /> when nothing maps that alias.
    /// A folder configuration does not name is a folder MailFathom does not have, so what it stored earlier is inert
    /// rather than readable: an operator who removes a mapping withdraws the folder, and one who wants its mail back
    /// maps it again.
    /// </returns>
    MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias);
}

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
/// The two exclusion lists exist beside <see cref="GetParticipation" /> because a query cannot ask one folder at a time.
/// A read narrows a table and needs the whole excluded set as a value it can put into a predicate, while a write path
/// holds one email and asks about that email's folder; both answers come from the same configuration, so neither can
/// drift from the other.
/// </para>
/// </remarks>
public interface IMailFolderParticipationReader
{
    /// <summary>Gets the folders no MCP tool may list, search, read, or answer from.</summary>
    IReadOnlyList<MailFolderIdentity> FoldersHiddenFromTools { get; }

    /// <summary>Gets the folders whose content is never cut into passages and never reaches an embedding provider.</summary>
    IReadOnlyList<MailFolderIdentity> FoldersWithoutEmbeddings { get; }

    /// <summary>Gets what one folder takes part in.</summary>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns>
    /// The configured participation, or <see cref="MailFolderParticipation.Full" /> when nothing maps that alias.
    /// A folder configuration no longer names is stored mail nobody withdrew anything from, and treating it as withheld
    /// would hide a mailbox because an operator removed a mapping.
    /// </returns>
    MailFolderParticipation GetParticipation(MailAccountId accountId, MailFolderAlias folderAlias);
}

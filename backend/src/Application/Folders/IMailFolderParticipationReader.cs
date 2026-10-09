// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// This is the answer about one folder, for a write path that holds one email and asks about that email's folder. A
/// read narrows a table and needs the whole admitted set as a value it can put into a predicate, which
/// <see cref="IDeploymentMailFolders" /> answers from the account records. This answer reads the settings its scope was
/// prepared with until #2330 moves it, so the two agree within one convergence interval of the account's last write
/// committing, as <see cref="IDeploymentMailFolders" /> states.
/// </para>
/// </remarks>
public interface IMailFolderParticipationReader
{
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

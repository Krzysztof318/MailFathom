// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>What local state holds about one account's own folders.</summary>
/// <param name="Phase">Which copy of the mailbox is the truth.</param>
/// <param name="Folders">The account's live folders, empty for an account whose mailbox its source is the truth about.</param>
/// <param name="ErasedSourceAliases">The source folders whose corresponding local folder has been erased.</param>
public sealed record LocalMailFolderHolding(
    MailAccountCustodyPhase Phase,
    IReadOnlyList<LocalMailFolder> Folders,
    IReadOnlyList<MailFolderAlias> ErasedSourceAliases)
{
    /// <summary>Gets the rules the account's folders are acted on by.</summary>
    /// <returns>The tree of the live folders.</returns>
    public LocalMailFolderTree ToTree() => new(this.Folders, this.ErasedSourceAliases);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Narrows a query over folder bindings to one mailbox scope, once for every read that reports on folders.</summary>
/// <remarks>
/// <para>
/// Written once for the reason <see cref="StoredMailInScope" /> is: the reads that report on a caller's folders must
/// admit exactly the same folders, and two predicates that happened to agree today would drift the first time either
/// learned something about a withheld folder or about a repointed alias. A reader that admitted one folder more than
/// its neighbour would be naming a folder to a caller who may not read it.
/// </para>
/// <para>
/// It is the folder counterpart of <see cref="StoredEmailSelectionPredicate" />'s own scope narrowing and applies the
/// same three decisions in the same order: the accounts the scope names, the folders a request selected, the folders
/// configuration admits, and the junk folder unless the caller asked for it. What it never applies is a tombstone rule,
/// because a binding row is not mail.
/// </para>
/// </remarks>
internal static class MailFoldersInScope
{
    /// <summary>Admits the folder bindings of one scope, which is every folder a caller holding it may be told about.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <returns>The narrowed query, still composed as <see cref="IQueryable{T}" /> so PostgreSQL does the filtering.</returns>
    /// <remarks>
    /// A folder this read returns nothing from is withheld from every fact about it, not only from its mail. Naming a
    /// folder is publishing that it exists, which is exactly what a caller must not learn about a folder they may not
    /// read — and what would name the junk folder to a caller who did not ask for it, or a folder no mapping names to a
    /// caller whose deployment no longer has it.
    /// </remarks>
    internal static IQueryable<MailFolderEntity> Within(IQueryable<MailFolderEntity> folders, MailboxScope scope)
    {
        if (scope.AccountIds.Count > 0)
        {
            var accountIds = scope.AccountIds.Select(static accountId => accountId.Value).ToArray();
            folders = folders.Where(folder => accountIds.Contains(folder.MailboxAccountId));
        }

        folders = AccountScopedMailFolders.Selecting(folders, scope.SelectedFolders);
        folders = AccountScopedMailFolders.Admitting(folders, scope.ReadableFolders);

        return AccountScopedMailFolders.Excluding(folders, scope.WithheldJunkFolders);
    }
}

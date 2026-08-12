// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Narrows a query by the folders a configuration decision has taken out of it.</summary>
/// <remarks>
/// <para>
/// A folder decision is made about an account and an alias together, and no column holds that pair, so the narrowing is
/// written once here rather than at each query that needs it. One clause is composed per account that excludes
/// something — <c>account &lt;&gt; 'a' OR alias &lt;&gt; ALL(…)</c> — which is bounded by how many accounts are
/// configured and disappears entirely when nothing is excluded, so the common deployment pays nothing for it.
/// </para>
/// <para>
/// Narrowing by the alias alone would be the silent mistake this exists to prevent: one account's hidden folder would
/// hide another account's folder of the same name, which is the wrong mail withheld for a reason nobody could find.
/// </para>
/// </remarks>
internal static class ExcludedMailFolders
{
    /// <summary>Narrows stored emails to the ones outside the excluded folders.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="excluded">The folders to leave out, empty for none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<StoredEmailEntity> Excluding(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> excluded)
    {
        foreach (var (accountId, aliases) in AliasesByAccount(excluded))
        {
            emails = emails.Where(email =>
                email.MailboxAccountId != accountId || !aliases.Contains(email.MailFolder.Alias));
        }

        return emails;
    }

    /// <summary>Narrows folder bindings to the ones outside the excluded folders.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="excluded">The folders to leave out, empty for none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderEntity> Excluding(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyList<MailFolderIdentity> excluded)
    {
        foreach (var (accountId, aliases) in AliasesByAccount(excluded))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || !aliases.Contains(folder.Alias));
        }

        return folders;
    }

    /// <summary>Reports whether the excluded set names one folder.</summary>
    /// <param name="excluded">The excluded folders.</param>
    /// <param name="accountId">The account the folder belongs to.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <returns><see langword="true" /> when the pair is excluded.</returns>
    internal static bool Contains(
        IReadOnlyList<MailFolderIdentity> excluded,
        string accountId,
        string folderAlias) => excluded.Any(folder =>
            StringComparer.Ordinal.Equals(folder.AccountId.Value, accountId)
            && StringComparer.Ordinal.Equals(folder.Alias.Value, folderAlias));

    private static IEnumerable<(string AccountId, string[] Aliases)> AliasesByAccount(
        IReadOnlyList<MailFolderIdentity> excluded) => excluded
            .GroupBy(folder => folder.AccountId.Value, StringComparer.Ordinal)
            .Select(static account => (account.Key, Aliases: account.Select(static folder => folder.Alias.Value).ToArray()));
}

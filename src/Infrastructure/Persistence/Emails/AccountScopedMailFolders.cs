// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Narrows a query by folders named as an account and an alias together, in either direction.</summary>
/// <remarks>
/// <para>
/// A folder decision is made about an account and an alias together, and no column holds that pair, so the narrowing is
/// written once here rather than at each query that needs it. One clause is composed per account the decision reaches —
/// <c>account &lt;&gt; 'a' OR alias &lt;&gt; ALL(…)</c> to withhold, <c>account &lt;&gt; 'a' OR alias = ANY(…)</c> to
/// select — which is bounded by how many accounts are configured and disappears entirely when the decision reaches
/// none, so the common deployment pays nothing for it.
/// </para>
/// <para>
/// Narrowing by the alias alone would be the silent mistake this exists to prevent, and it fails differently on each
/// side. Withholding by name alone hides another account's folder of the same name, which is the wrong mail withheld
/// for a reason nobody could find. Selecting by name alone admits another account's folder of the same name, which is
/// mail the caller's own filter never asked for — the case a folder named by the role it plays produces, because a role
/// resolves to a different alias on each account.
/// </para>
/// </remarks>
internal static class AccountScopedMailFolders
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

    /// <summary>Narrows stored emails to the folders a scope selected, each within the account that selected it.</summary>
    /// <param name="emails">The emails to narrow.</param>
    /// <param name="selected">The folders to keep, empty when the request named none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// Two statements rather than one: the accounts that selected something at all, then one clause per such account
    /// keeping its own aliases. The first is what makes an account of the scope that selected no folder admit nothing,
    /// which is what an account mapping no folder for a requested role has to mean, and it is also what keeps this
    /// narrowing from depending on a caller having applied the account filter first. The second is the same implication
    /// the exclusions above are written as, and composes the same way, because a row belongs to one account and
    /// therefore meets exactly one non-vacuous clause.
    /// </remarks>
    internal static IQueryable<StoredEmailEntity> Selecting(
        IQueryable<StoredEmailEntity> emails,
        IReadOnlyList<MailFolderIdentity> selected)
    {
        if (selected.Count == 0)
        {
            return emails;
        }

        var selectedAccounts = AccountsOf(selected);
        emails = emails.Where(email => selectedAccounts.Contains(email.MailboxAccountId));

        foreach (var (accountId, aliases) in AliasesByAccount(selected))
        {
            emails = emails.Where(email =>
                email.MailboxAccountId != accountId || aliases.Contains(email.MailFolder.Alias));
        }

        return emails;
    }

    /// <summary>Narrows folder bindings to the folders a scope selected, read the same way as above.</summary>
    /// <param name="folders">The bindings to narrow.</param>
    /// <param name="selected">The folders to keep, empty when the request named none.</param>
    /// <returns>The narrowed query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<MailFolderEntity> Selecting(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyList<MailFolderIdentity> selected)
    {
        if (selected.Count == 0)
        {
            return folders;
        }

        var selectedAccounts = AccountsOf(selected);
        folders = folders.Where(folder => selectedAccounts.Contains(folder.MailboxAccountId));

        foreach (var (accountId, aliases) in AliasesByAccount(selected))
        {
            folders = folders.Where(folder =>
                folder.MailboxAccountId != accountId || aliases.Contains(folder.Alias));
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

    private static string[] AccountsOf(IReadOnlyList<MailFolderIdentity> folders) =>
    [
        .. folders
            .Select(static folder => folder.AccountId.Value)
            .Distinct(StringComparer.Ordinal),
    ];

    private static IEnumerable<(string AccountId, string[] Aliases)> AliasesByAccount(
        IReadOnlyList<MailFolderIdentity> excluded) => excluded
            .GroupBy(folder => folder.AccountId.Value, StringComparer.Ordinal)
            .Select(static account => (account.Key, Aliases: account.Select(static folder => folder.Alias.Value).ToArray()));
}

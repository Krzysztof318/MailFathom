// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Decides which accounts and folders a mailbox read actually runs against.</summary>
/// <remarks>
/// Every read model asks the same two questions of a requested scope, and both answers are access decisions rather than
/// query details, which is why they are resolved once here instead of per use case. A read model that answered either
/// one differently would publish mail through the query that got it wrong while the others stayed correct.
/// </remarks>
public sealed class MailboxScopeResolver
{
    private readonly IMailAccountCatalog accountCatalog;

    /// <summary>Initializes the resolver.</summary>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountCatalog" /> is <see langword="null" />.</exception>
    public MailboxScopeResolver(IMailAccountCatalog accountCatalog)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);

        this.accountCatalog = accountCatalog;
    }

    /// <summary>Resolves what a request named into the scope a query runs with.</summary>
    /// <param name="accountSelectors">The text a request named accounts with, or empty for every served account.</param>
    /// <param name="folderAliases">The folder aliases a request named, or empty for every folder.</param>
    /// <returns>The scope a query runs with.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more values than its limit permits.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <remarks>
    /// <para>
    /// An account may be named by its configured identifier or by the display name it is published under, and this is
    /// where the two become one identity. Resolution happens against the served accounts rather than at a protocol
    /// boundary, so text naming nothing is refused by the same rule and with the same failure as an identifier the
    /// deployment stopped serving — a caller cannot learn from the refusal which spelling it was holding.
    /// </para>
    /// <para>
    /// An account the deployment does not serve is refused before anything is read, rather than narrowed away by a
    /// predicate: a narrowed predicate would answer with an empty result, and an empty result tells a caller that the
    /// name they used exists. The resolved identities are what the scope is built from, so the same account named two
    /// ways is one query with one continuation cursor.
    /// </para>
    /// <para>
    /// A request that names no account is restricted to the served accounts rather than left unrestricted. Removing an
    /// account from configuration leaves its stored rows in place, so an absent account predicate would keep publishing
    /// mail from an account this deployment no longer serves — which is also why the resolved accounts, not the
    /// requested ones, take part in a continuation cursor's fingerprint.
    /// </para>
    /// </remarks>
    public MailboxScope ReadableScope(
        IReadOnlyList<MailAccountSelector> accountSelectors,
        IReadOnlyList<MailFolderAlias> folderAliases)
    {
        ArgumentNullException.ThrowIfNull(accountSelectors);

        var servedAccounts = this.accountCatalog.ServedAccounts;

        // Counted before anything is resolved, because the count is the caller's and each resolution walks the served
        // accounts. The scope's own limit is reused rather than a second one invented for the text the identities
        // arrive as.
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            accountSelectors.Count,
            MailboxScope.MaximumAccountIds,
            "accounts");

        var requestedScope = MailboxScope.Create(
            [.. accountSelectors.Select(selector => ResolvedAccountId(selector, servedAccounts))],
            folderAliases);

        return requestedScope.AccountIds.Count is 0
            ? MailboxScope.RestrictedToServedAccounts(
                servedAccounts.Select(static account => account.Id),
                requestedScope.FolderAliases)
            : requestedScope;
    }

    /// <summary>Finds the served account text names, refusing the request when it names none.</summary>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when no served account carries that identifier or display name.</exception>
    private static MailAccountId ResolvedAccountId(
        MailAccountSelector selector,
        IReadOnlyList<ServedMailAccount> servedAccounts) =>
        servedAccounts.FirstOrDefault(account => account.IsNamedBy(selector))?.Id
        ?? throw new MailAccountNotAccessibleException(selector);
}

// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Emails;

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

    /// <summary>Normalizes a requested scope and restricts it to the accounts this deployment serves.</summary>
    /// <param name="accountIds">The accounts a request named, or empty for every served account.</param>
    /// <param name="folderAliases">The folder aliases a request named, or empty for every folder.</param>
    /// <returns>The scope a query runs with.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more values than its limit permits.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <remarks>
    /// <para>
    /// An account the deployment does not serve is refused before anything is read, rather than narrowed away by a
    /// predicate: a narrowed predicate would answer with an empty result, and an empty result tells a caller that the
    /// identifier they named exists. The check runs against the normalized scope, so the same request cannot be written
    /// two ways to reach two answers.
    /// </para>
    /// <para>
    /// A request that names no account is restricted to the served accounts rather than left unrestricted. Removing an
    /// account from configuration leaves its stored rows in place, so an absent account predicate would keep publishing
    /// mail from an account this deployment no longer serves — which is also why the resolved accounts, not the
    /// requested ones, take part in a continuation cursor's fingerprint.
    /// </para>
    /// </remarks>
    public MailboxScope ReadableScope(
        IReadOnlyList<MailAccountId> accountIds,
        IReadOnlyList<MailFolderAlias> folderAliases)
    {
        var requestedScope = MailboxScope.Create(accountIds, folderAliases);
        var servedAccountIds = this.accountCatalog.ServedAccountIds;

        if (FirstAccountNotServed(requestedScope, servedAccountIds) is { } inaccessibleAccountId)
        {
            throw new MailAccountNotAccessibleException(inaccessibleAccountId);
        }

        return requestedScope.AccountIds.Count is 0
            ? MailboxScope.RestrictedToServedAccounts(servedAccountIds, requestedScope.FolderAliases)
            : requestedScope;
    }

    private static MailAccountId? FirstAccountNotServed(
        MailboxScope requestedScope,
        IReadOnlyList<MailAccountId> servedAccountIds) => requestedScope.AccountIds
        .Select(static accountId => (MailAccountId?)accountId)
        .FirstOrDefault(accountId => !servedAccountIds.Contains(accountId!.Value));
}

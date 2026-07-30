// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Emails;

/// <summary>Names the accounts and folder aliases a mailbox query is restricted to.</summary>
/// <remarks>
/// <para>
/// Naming nothing restricts nothing: an empty list of accounts means every account this deployment serves, and an empty
/// list of aliases means every folder of those accounts. That is why absence and an empty list are the same input here
/// rather than two states — a request that omits a scope has not asked for a narrower one.
/// </para>
/// <para>
/// Both lists are deduplicated and ordered when the scope is created, so two requests that name the same accounts in a
/// different order are one query with one continuation cursor. The cursor's filter fingerprint is computed from this
/// normalized form, which is what stops a client from being handed a mismatch for reordering its own input.
/// </para>
/// </remarks>
public sealed record MailboxScope
{
    /// <summary>The greatest number of distinct accounts one query may name.</summary>
    /// <remarks>
    /// The bound exists because the scope reaches a database predicate and a caller supplies it. It is generous against
    /// any deployment's configured account count, so meeting it means a request enumerated identifiers rather than
    /// described a mailbox.
    /// </remarks>
    public const int MaximumAccountIds = 64;

    /// <summary>The greatest number of distinct folder aliases one query may name.</summary>
    public const int MaximumFolderAliases = 64;

    private MailboxScope(IReadOnlyList<MailAccountId> accountIds, IReadOnlyList<MailFolderAlias> folderAliases)
    {
        this.AccountIds = accountIds;
        this.FolderAliases = folderAliases;
    }

    /// <summary>Gets the scope that restricts nothing, covering every served account and every folder.</summary>
    public static MailboxScope Unrestricted { get; } = new([], []);

    /// <summary>Gets the accounts the query is restricted to, deduplicated and ordered, or empty for every served account.</summary>
    public IReadOnlyList<MailAccountId> AccountIds { get; }

    /// <summary>Gets the folder aliases the query is restricted to, deduplicated and ordered, or empty for every folder.</summary>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; }

    /// <summary>Gets whether the scope names neither an account nor a folder and therefore restricts nothing.</summary>
    public bool IsUnrestricted => this.AccountIds.Count is 0 && this.FolderAliases.Count is 0;

    /// <summary>Creates a normalized scope from what a request named.</summary>
    /// <param name="accountIds">The accounts to restrict to, or <see langword="null" /> to restrict to none.</param>
    /// <param name="folderAliases">The folder aliases to restrict to, or <see langword="null" /> to restrict to none.</param>
    /// <returns>The scope, with both lists deduplicated and ordered.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more distinct values than its limit permits.</exception>
    public static MailboxScope Create(
        IEnumerable<MailAccountId>? accountIds,
        IEnumerable<MailFolderAlias>? folderAliases)
    {
        var distinctAccountIds = Canonical(accountIds, static accountId => accountId.Value);
        var distinctFolderAliases = Canonical(folderAliases, static alias => alias.Value);

        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            distinctAccountIds.Length,
            MaximumAccountIds,
            "accounts");
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            distinctFolderAliases.Length,
            MaximumFolderAliases,
            "folder aliases");

        return distinctAccountIds.Length is 0 && distinctFolderAliases.Length is 0
            ? Unrestricted
            : new MailboxScope(distinctAccountIds, distinctFolderAliases);
    }

    private static TValue[] Canonical<TValue>(IEnumerable<TValue>? values, Func<TValue, string> text) =>
    [
        .. (values ?? [])
            .DistinctBy(text, StringComparer.Ordinal)
            .OrderBy(text, StringComparer.Ordinal),
    ];
}

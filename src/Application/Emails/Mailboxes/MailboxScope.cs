// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Names the accounts and folder aliases a mailbox query is restricted to.</summary>
/// <remarks>
/// <para>
/// A request that names no folder alias is restricted to none: an empty list of aliases means every folder of the
/// accounts in scope. Accounts read differently once the scope reaches a query, because the accounts a deployment serves
/// are a smaller set than the accounts a store holds rows for — an operator can remove an account whose mail is still
/// stored. <see cref="Create" /> therefore states what a request asked for, and the use case resolves an unnamed account
/// list to the served accounts through <see cref="RestrictedToServedAccounts" /> before anything is read.
/// </para>
/// <para>
/// Both lists are deduplicated and ordered when the scope is created, so two requests that name the same accounts in a
/// different order are one query with one continuation cursor. The cursor's filter fingerprint is computed from this
/// normalized form, which is what stops a client from being handed a mismatch for reordering its own input.
/// </para>
/// </remarks>
public sealed record MailboxScope
{
    /// <summary>The greatest number of account identifiers one request may name.</summary>
    /// <remarks>
    /// The bound exists because the scope reaches a database predicate and a caller supplies it. It counts the
    /// identifiers a request names rather than the distinct ones left after deduplication, so the limit can be enforced
    /// while the caller's sequence is read instead of after it has been materialized. It is generous against any
    /// deployment's configured account count, so meeting it means a request enumerated identifiers rather than described
    /// a mailbox.
    /// </remarks>
    public const int MaximumAccountIds = 64;

    /// <summary>The greatest number of folder aliases one request may name, counted the same way as <see cref="MaximumAccountIds" />.</summary>
    public const int MaximumFolderAliases = 64;

    private MailboxScope(IReadOnlyList<MailAccountId> accountIds, IReadOnlyList<MailFolderAlias> folderAliases)
    {
        this.AccountIds = accountIds;
        this.FolderAliases = folderAliases;
    }

    /// <summary>Gets the scope a request that named neither an account nor a folder produces.</summary>
    /// <remarks>
    /// It is what a caller asked for rather than what a query runs with. A mailbox query resolves its accounts before it
    /// reads, so an empty account list never reaches storage as an absent predicate.
    /// </remarks>
    public static MailboxScope Unrestricted { get; } = new([], []);

    /// <summary>Gets the accounts the query is restricted to, deduplicated and ordered, or empty when the request named none.</summary>
    public IReadOnlyList<MailAccountId> AccountIds { get; }

    /// <summary>Gets the folder aliases the query is restricted to, deduplicated and ordered, or empty for every folder.</summary>
    public IReadOnlyList<MailFolderAlias> FolderAliases { get; }

    /// <summary>Gets the folders no query built from this scope may return anything from, ordered, or empty when none is withheld.</summary>
    /// <remarks>
    /// It is the opposite kind of value from the two above: those are what a caller asked for, this is what configuration
    /// withholds whatever the caller asked for. A request that names a hidden folder explicitly is therefore answered
    /// with nothing from it rather than refused, which is the same answer a folder that holds no matching mail gives — a
    /// refusal would tell the caller the folder is there.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> HiddenFolders { get; private init; } = [];

    /// <summary>Creates a normalized scope from what a request named.</summary>
    /// <param name="accountIds">The accounts to restrict to, or <see langword="null" /> to name none.</param>
    /// <param name="folderAliases">The folder aliases to restrict to, or <see langword="null" /> to name none.</param>
    /// <returns>The scope, with both lists deduplicated and ordered.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when either list names more values than its limit permits.</exception>
    public static MailboxScope Create(
        IEnumerable<MailAccountId>? accountIds,
        IEnumerable<MailFolderAlias>? folderAliases)
    {
        var requestedAccountIds = Canonical(
            accountIds,
            static accountId => accountId.Value,
            MaximumAccountIds,
            "accounts");
        var requestedFolderAliases = Canonical(
            folderAliases,
            static alias => alias.Value,
            MaximumFolderAliases,
            "folder aliases");

        return requestedAccountIds.Length is 0 && requestedFolderAliases.Length is 0
            ? Unrestricted
            : new MailboxScope(requestedAccountIds, requestedFolderAliases);
    }

    /// <summary>Creates the scope a query runs with, naming the accounts a deployment serves rather than the ones a request asked for.</summary>
    /// <param name="servedAccountIds">The accounts the deployment serves, which the caller did not supply.</param>
    /// <param name="folderAliases">The folder aliases the request named, already normalized.</param>
    /// <returns>The scope, with the served accounts deduplicated and ordered.</returns>
    /// <remarks>
    /// No count limit applies. The account list is configuration rather than caller input, so a deployment that serves
    /// more accounts than a request may name still answers a request that names none, and the limit that exists to bound
    /// untrusted input would otherwise refuse it.
    /// </remarks>
    internal static MailboxScope RestrictedToServedAccounts(
        IEnumerable<MailAccountId> servedAccountIds,
        IReadOnlyList<MailFolderAlias> folderAliases) => new(
        [
            .. servedAccountIds
                .DistinctBy(static accountId => accountId.Value, StringComparer.Ordinal)
                .OrderBy(static accountId => accountId.Value, StringComparer.Ordinal),
        ],
        folderAliases);

    /// <summary>Withholds the folders configuration says no tool may read from.</summary>
    /// <param name="hiddenFolders">The folders to withhold, empty when none is.</param>
    /// <returns>The same scope with the folders withheld, or this scope unchanged when none is.</returns>
    /// <remarks>
    /// Ordered and deduplicated so one configuration produces one predicate, whichever order the folders were read in.
    /// It deliberately does not take part in a continuation cursor's fingerprint, unlike the two requested lists: those
    /// are the caller's filters, and a walk resumed under different ones would return a page that does not follow the
    /// previous one. Hiding a folder only removes rows from an unchanged ordering, so an outstanding cursor stays
    /// consistent, and unhiding one adds rows the walk may already have passed — which is what a keyset walk over a
    /// live mailbox does about newly arrived mail anyway.
    /// </remarks>
    internal MailboxScope Hiding(IReadOnlyList<MailFolderIdentity> hiddenFolders) => hiddenFolders.Count == 0
        ? this
        : this with
        {
            HiddenFolders =
            [
                .. hiddenFolders
                    .DistinctBy(static folder => (folder.AccountId.Value, folder.Alias.Value))
                    .OrderBy(static folder => folder.AccountId.Value, StringComparer.Ordinal)
                    .ThenBy(static folder => folder.Alias.Value, StringComparer.Ordinal),
            ],
        };

    /// <summary>Deduplicates and orders one requested list, refusing it as soon as it names more values than the limit.</summary>
    /// <remarks>
    /// The limit is checked while the sequence is read rather than over the result, which is a loop rather than a query
    /// because a pipeline would have to enumerate the whole input before its count could be refused. A request that
    /// repeats one identifier a million times is therefore rejected after reading the value that crosses the limit, and
    /// nothing beyond it is ever materialized.
    /// </remarks>
    private static TValue[] Canonical<TValue>(
        IEnumerable<TValue>? values,
        Func<TValue, string> text,
        int limit,
        string filterName)
    {
        var namedCount = 0;
        var byText = new SortedDictionary<string, TValue>(StringComparer.Ordinal);

        foreach (var value in values ?? [])
        {
            MailboxQueryFilterInvalidException.ThrowIfCountExceeded(++namedCount, limit, filterName);

            byText.TryAdd(text(value), value);
        }

        return [.. byText.Values];
    }
}

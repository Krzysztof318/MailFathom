// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Names the accounts and the folders of those accounts a mailbox query is restricted to.</summary>
/// <remarks>
/// <para>
/// This is what a query runs with rather than what a request asked for. Both differences are settled before the scope is
/// built: an unnamed account list becomes the accounts this deployment serves, because a store holds rows for accounts
/// an operator has since removed, and every folder a request named — by its alias or by the role it plays — becomes the
/// folder of the account it means. A folder is therefore named here as an account and an alias together, never as an
/// alias alone, so one account's junk folder cannot admit another account's folder that happens to share the name.
/// </para>
/// <para>
/// A request that names no folder is restricted to none: an empty folder list means every folder of the accounts in
/// scope. An account in scope that appears in no pair while others do is the opposite case — it maps no folder the
/// request named, and contributes nothing.
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
    /// identifiers a request names rather than the distinct ones left after deduplication, so a request cannot buy a
    /// larger predicate by repeating one name. It is generous against any deployment's configured account count, so
    /// meeting it means a request enumerated identifiers rather than described a mailbox.
    /// </remarks>
    public const int MaximumAccountIds = 64;

    /// <summary>The greatest number of folders one request may name, counted the same way as <see cref="MaximumAccountIds" />.</summary>
    /// <remarks>
    /// It counts what the caller wrote rather than folders, which is the same distinction the bound above makes and one
    /// a role sharpens: <c>role:Junk</c> is one name however many accounts answer it. Both bounds are enforced where the
    /// caller's own list is read, in <see cref="MailboxScopeResolver" />, rather than over the resolved lists this type
    /// holds. Resolution can only produce more: a request naming no account resolves to every served account, and one
    /// role a request named resolves to a folder on each of them.
    /// </remarks>
    public const int MaximumFolders = 64;

    private MailboxScope(IReadOnlyList<MailAccountId> accountIds, IReadOnlyList<MailFolderIdentity> selectedFolders)
    {
        this.AccountIds = accountIds;
        this.SelectedFolders = selectedFolders;
    }

    /// <summary>Gets the scope that restricts nothing, which is what a deployment serving no account resolves to.</summary>
    /// <remarks>
    /// A read never runs against it in a deployment that serves accounts, because resolution replaces an unnamed account
    /// list with the served ones before the scope is built. A use case handed it therefore answers with nothing rather
    /// than reading the store unrestricted.
    /// </remarks>
    public static MailboxScope Unrestricted { get; } = new([], []);

    /// <summary>Gets the accounts the query is restricted to, deduplicated and ordered, or empty when the request named none.</summary>
    public IReadOnlyList<MailAccountId> AccountIds { get; }

    /// <summary>Gets the folders the query is restricted to as account-and-alias pairs, deduplicated and ordered, or empty for every folder.</summary>
    /// <remarks>
    /// <para>
    /// A folder named by alias is paired with every account in scope, because an alias is the caller's own name and
    /// means the same folder on each of them. A folder named by role is paired with the account that answered for it,
    /// because a role means a different folder on each account: <c>role:Junk</c> across two accounts is two pairs
    /// rather than two names either account might carry. Reading the aliases alone would admit a folder of the second
    /// account that happens to share the first account's junk alias and plays no role at all — the same mistake
    /// <c>AccountScopedMailFolders</c> exists to prevent on the withholding side.
    /// </para>
    /// <para>
    /// An account of <see cref="AccountIds" /> that appears in no pair while the list is non-empty contributes nothing,
    /// which is what an account mapping no folder for a named role means. That is why a query reads this against the
    /// accounts in scope rather than against the pairs alone.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> SelectedFolders { get; }

    /// <summary>Gets the folders no query built from this scope may return anything from, ordered, or empty when none is withheld.</summary>
    /// <remarks>
    /// It is the opposite kind of value from the two above: those are what a caller asked for, this is what configuration
    /// withholds whatever the caller asked for. A request that names a hidden folder explicitly is therefore answered
    /// with nothing from it rather than refused, which is the same answer a folder that holds no matching mail gives — a
    /// refusal would tell the caller the folder is there.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> HiddenFolders { get; private init; } = [];

    /// <summary>Gets the junk folders this read returns nothing from, ordered, or empty when the caller asked for junk or no account maps one.</summary>
    /// <remarks>
    /// Kept apart from <see cref="HiddenFolders" /> although both narrow the same query, because the two answer different
    /// questions and only one of them can be overridden. A hidden folder is withheld from every tool by an operator's
    /// decision and no caller may reach it; the junk folder is withheld by default and reached by a caller that asks.
    /// Merging them would make the override able to reveal a folder the operator withheld.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> WithheldJunkFolders { get; private init; } = [];

    /// <summary>Gets whether the caller asked for the junk folder's mail.</summary>
    /// <remarks>
    /// Unlike the two withheld lists, this is the caller's own decision, so it takes part in a continuation cursor's
    /// fingerprint: including junk adds rows in the middle of an ordering, which a walk resumed under the other answer
    /// would either skip or repeat. The lists themselves stay out of the fingerprint for the reason
    /// <see cref="Hiding" /> gives — they are configuration, and a reload must not invalidate an outstanding cursor.
    /// </remarks>
    public bool IncludesJunkMail { get; private init; }

    /// <summary>Gets every folder this read returns nothing from, whichever decision withheld it.</summary>
    /// <remarks>
    /// What a query needs is the union, because a predicate does not care why a folder is out. Both callers of it — the
    /// stored-email predicate and the folder freshness read — take this rather than either list, so a folder cannot be
    /// withheld from the mail and still named in the freshness beside it.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> WithheldFolders =>
    [
        .. this.HiddenFolders
            .Concat(this.WithheldJunkFolders)
            .DistinctBy(static folder => (folder.AccountId.Value, folder.Alias.Value)),
    ];

    /// <summary>Creates the scope a query runs with, from identities already resolved against configuration.</summary>
    /// <param name="accountIds">The accounts the query runs against, which are the served ones when a request named none.</param>
    /// <param name="selectedFolders">The folders the query runs against, one pair per account, with every role a request named already turned into the folder it means on that account, or <see langword="null" /> to name none.</param>
    /// <returns>The scope, with both lists deduplicated and ordered.</returns>
    /// <remarks>
    /// No count limit applies to either list, and the two constants above say where the limits are enforced instead.
    /// Both lists are configuration read through a resolution rather than caller input: a deployment that serves more
    /// accounts than a request may name still answers a request that names none, and one role a request named can mean
    /// a folder on each of those accounts.
    /// </remarks>
    public static MailboxScope Create(
        IEnumerable<MailAccountId>? accountIds,
        IEnumerable<MailFolderIdentity>? selectedFolders)
    {
        MailAccountId[] accounts =
        [
            .. (accountIds ?? [])
                .DistinctBy(static accountId => accountId.Value, StringComparer.Ordinal)
                .OrderBy(static accountId => accountId.Value, StringComparer.Ordinal),
        ];
        MailFolderIdentity[] folders =
        [
            .. (selectedFolders ?? [])
                .DistinctBy(static folder => (folder.AccountId.Value, folder.Alias.Value))
                .OrderBy(static folder => folder.AccountId.Value, StringComparer.Ordinal)
                .ThenBy(static folder => folder.Alias.Value, StringComparer.Ordinal),
        ];

        return accounts.Length is 0 && folders.Length is 0
            ? Unrestricted
            : new MailboxScope(accounts, folders);
    }

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

    /// <summary>Applies the caller's answer about junk mail to the junk folders configuration maps.</summary>
    /// <param name="inclusion">What the caller asked for.</param>
    /// <param name="junkFolders">Every account's junk folder, empty when none is mapped.</param>
    /// <returns>The same scope with the junk folders withheld, or with the caller's inclusion recorded and nothing withheld.</returns>
    /// <remarks>
    /// The inclusion is recorded either way, including when no account maps a junk folder, because it is part of what a
    /// continuation cursor was issued for. Recording it only when something was withheld would let a cursor issued
    /// before a junk mapping existed be presented after one was added.
    /// </remarks>
    internal MailboxScope WithJunkMail(
        JunkMailInclusion inclusion,
        IReadOnlyList<MailFolderIdentity> junkFolders) => this with
        {
            IncludesJunkMail = inclusion is JunkMailInclusion.Included,
            WithheldJunkFolders = inclusion is JunkMailInclusion.Included
                ? []
                :
                [
                    .. junkFolders
                        .DistinctBy(static folder => (folder.AccountId.Value, folder.Alias.Value))
                        .OrderBy(static folder => folder.AccountId.Value, StringComparer.Ordinal)
                        .ThenBy(static folder => folder.Alias.Value, StringComparer.Ordinal),
                ],
        };
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>Names the accounts and the folders of those accounts a mailbox query is restricted to.</summary>
/// <remarks>
/// <para>
/// This is what a query runs with rather than what a request asked for. Both differences are settled before the scope is
/// built: an unnamed account list becomes the accounts the caller's owner owns, because a store holds rows for accounts
/// an operator has since removed and rows belonging to every other owner, and every folder a request named — by its
/// alias or by the role it plays — becomes the folder of the account it means. A folder is therefore named here as an account and an alias together, never as an
/// alias alone, so one account's junk folder cannot admit another account's folder that happens to share the name.
/// </para>
/// <para>
/// A request that names no folder is restricted to none: an empty folder list means every folder of the accounts in
/// scope, which is every folder configuration admits rather than every folder the store holds rows for. An account in
/// scope that appears in no pair while others do is the opposite case — it maps no folder the request named, and
/// contributes nothing.
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
    /// holds. Resolution can only produce more: a request naming no account resolves to every account its owner owns,
    /// and one role a request named resolves to a folder on each of them.
    /// </remarks>
    public const int MaximumFolders = 64;

    /// <summary>The greatest number of individual emails a question may be asked about.</summary>
    /// <remarks>
    /// Generous against what a person selects in a list before asking about the selection, so meeting it means a caller
    /// enumerated a mailbox rather than chose messages. It is enforced in <see cref="NarrowedToEmails" /> rather than at
    /// a transport, because unlike the two bounds above this list reaches the scope as the caller wrote it.
    /// </remarks>
    public const int MaximumSelectedEmails = 64;

    private MailboxScope(
        MailOwnerId owner,
        IReadOnlyList<MailAccountId> accountIds,
        IReadOnlyList<MailFolderIdentity> selectedFolders)
    {
        this.Owner = owner;
        this.AccountIds = accountIds;
        this.SelectedFolders = selectedFolders;
    }

    /// <summary>Gets the scope that names no account, no folder, and nothing readable.</summary>
    /// <remarks>
    /// <para>
    /// Two callers resolve to it. A deployment serving no account is the first and the older one. The second is a
    /// caller acting for an owner who owns none of the accounts the deployment does serve, which is a real read on a
    /// populated deployment rather than a degenerate configuration — <see cref="MailboxScopeResolver" /> answers it
    /// with this scope before any folder decision is applied, because those decisions are the deployment's and would
    /// otherwise admit every other owner's folders beside an account list that names nobody.
    /// </para>
    /// <para>
    /// A use case handed it answers with nothing, which is what both cases have to mean: there is no folder this caller
    /// may read. What it must never become is an unrestricted query, and that is why it names no folder as well as no
    /// account — an empty account list alone is read as every account by the persistence predicate.
    /// </para>
    /// </remarks>
    public static MailboxScope NothingReadable { get; } = new(default, [], []);

    /// <summary>Gets the owner whose mail the query is restricted to, which names nobody on <see cref="NothingReadable" />.</summary>
    /// <remarks>
    /// <para>
    /// The first term of every mail-returning query, ahead of the accounts and applied whatever the account list holds.
    /// An account identifier names one account within its owner rather than across the deployment, so a read narrowed
    /// on the account alone would compare a value that does not say whose mail it is; and every index those reads are
    /// planned against leads with this column, so it is what the plan is chosen for as well.
    /// </para>
    /// <para>
    /// On <see cref="NothingReadable" /> it is the unspecified identity, which names nobody and therefore matches no
    /// row. That is a second reason such a scope reads nothing, beside its admitting no folder, and it is what makes the
    /// empty account list — which every narrowing site reads as unrestricted — safe rather than merely unreachable.
    /// </para>
    /// </remarks>
    public MailOwnerId Owner { get; }

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
    /// <c>AccountScopedMailFolders</c> exists to prevent on the admitting side.
    /// </para>
    /// <para>
    /// An account of <see cref="AccountIds" /> that appears in no pair while the list is non-empty contributes nothing,
    /// which is what an account mapping no folder for a named role means. That is why a query reads this against the
    /// accounts in scope rather than against the pairs alone.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> SelectedFolders { get; }

    /// <summary>Gets the only folders a query built from this scope may return anything from, ordered, or empty when none may.</summary>
    /// <remarks>
    /// <para>
    /// It is the opposite kind of value from the two above: those are what a caller asked for, this is what
    /// configuration admits whatever the caller asked for. A request that names a folder outside it is therefore
    /// answered with nothing from that folder rather than refused, which is the same answer a folder that holds no
    /// matching mail gives — a refusal would tell the caller the folder is there.
    /// </para>
    /// <para>
    /// Empty means no folder is readable, never that every folder is. A mapping is what makes MailFathom have a folder
    /// at all, so a scope nothing narrowed is a scope over a deployment that maps nothing, and reading it as an open
    /// mailbox would publish the rows of every folder an operator ever removed. <see cref="MailboxScopeResolver" />
    /// is what fills it, and a scope built any other way reads nothing by construction rather than by convention.
    /// </para>
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> ReadableFolders { get; private init; } = [];

    /// <summary>Gets the junk folders this read returns nothing from, ordered, or empty when the caller asked for junk or no account maps one.</summary>
    /// <remarks>
    /// Kept apart from <see cref="ReadableFolders" /> although both narrow the same query, because the two answer
    /// different questions and only one of them can be overridden. A folder outside the readable set is withheld by an
    /// operator's decision, or absent from configuration altogether, and no caller may reach it; the junk folder is
    /// mapped, readable, and withheld by default, and a caller that asks reaches it. Merging them would make the
    /// override able to reveal a folder the operator withheld.
    /// </remarks>
    public IReadOnlyList<MailFolderIdentity> WithheldJunkFolders { get; private init; } = [];

    /// <summary>Gets whether the caller asked for the junk folder's mail.</summary>
    /// <remarks>
    /// Unlike the readable and the withheld lists, this is the caller's own decision, so it takes part in a continuation
    /// cursor's fingerprint: including junk adds rows in the middle of an ordering, which a walk resumed under the other
    /// answer would either skip or repeat. The lists themselves stay out of the fingerprint for the reason
    /// <see cref="RestrictedTo" /> gives — they are configuration, and a reload must not invalidate an outstanding
    /// cursor.
    /// </remarks>
    public bool IncludesJunkMail { get; private init; }

    /// <summary>Gets the conversation a query is restricted to, or <see langword="null" /> when it is restricted to no single one.</summary>
    /// <remarks>
    /// The narrowing a question asked about one conversation carries. It is the caller's own filter rather than
    /// configuration, so it takes part in a continuation cursor's fingerprint for the reason
    /// <see cref="IncludesJunkMail" /> does: it removes rows from the middle of an ordering that a walk resumed without
    /// it would return.
    /// </remarks>
    public EmailThreadId? SelectedThread { get; private init; }

    /// <summary>Gets the individual emails a query is restricted to, deduplicated and ordered, or empty when it is restricted to none.</summary>
    /// <remarks>
    /// <para>
    /// The narrowing a question asked about a selection carries, and the one place a scope names mail rather than the
    /// containers holding it. Empty means no such narrowing, never that no email is readable — which is the opposite of
    /// how <see cref="ReadableFolders" /> reads, because that list is configuration and this one is a caller's choice.
    /// </para>
    /// <para>
    /// An identifier naming mail this scope's owner does not own matches nothing rather than being refused. The owner is
    /// the first term of every mail-returning query whatever else narrows it, so a caller cannot reach another owner's
    /// mail by naming its identifier, and answering with nothing is what naming an email that no longer exists already
    /// does.
    /// </para>
    /// </remarks>
    public IReadOnlyList<StoredEmailId> SelectedEmails { get; private init; } = [];

    /// <summary>Creates the scope a query runs with, from identities already resolved against configuration.</summary>
    /// <param name="owner">The owner whose mail the query is restricted to, which every narrowing then sits inside.</param>
    /// <param name="accountIds">The accounts the query runs against, which are the ones the caller's owner owns when a request named none.</param>
    /// <param name="selectedFolders">The folders the query runs against, one pair per account, with every role a request named already turned into the folder it means on that account, or <see langword="null" /> to name none.</param>
    /// <returns>The scope, with both lists deduplicated and ordered.</returns>
    /// <remarks>
    /// No count limit applies to either list, and the two constants above say where the limits are enforced instead.
    /// Both lists are configuration read through a resolution rather than caller input: a deployment that serves more
    /// accounts than a request may name still answers a request that names none, and one role a request named can mean
    /// a folder on each of those accounts.
    /// </remarks>
    public static MailboxScope Create(
        MailOwnerId owner,
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
            ? NothingReadable
            : new MailboxScope(owner, accounts, folders);
    }

    /// <summary>Admits the folders configuration says a tool may read from, and nothing else.</summary>
    /// <param name="readableFolders">The folders a tool may read, empty when none may.</param>
    /// <returns>The same scope restricted to those folders, or this scope unchanged when none is readable.</returns>
    /// <remarks>
    /// Ordered and deduplicated so one configuration produces one predicate, whichever order the folders were read in.
    /// It deliberately does not take part in a continuation cursor's fingerprint, unlike the two requested lists: those
    /// are the caller's filters, and a walk resumed under different ones would return a page that does not follow the
    /// previous one. Withdrawing a folder only removes rows from an unchanged ordering, so an outstanding cursor stays
    /// consistent, and admitting one adds rows the walk may already have passed — which is what a keyset walk over a
    /// live mailbox does about newly arrived mail anyway.
    /// </remarks>
    internal MailboxScope RestrictedTo(IReadOnlyList<MailFolderIdentity> readableFolders) => readableFolders.Count == 0
        ? this
        : this with
        {
            ReadableFolders =
            [
                .. readableFolders
                    .DistinctBy(static folder => (folder.AccountId.Value, folder.Alias.Value))
                    .OrderBy(static folder => folder.AccountId.Value, StringComparer.Ordinal)
                    .ThenBy(static folder => folder.Alias.Value, StringComparer.Ordinal),
            ],
        };

    /// <summary>Restricts the scope to one conversation, on top of everything already narrowing it.</summary>
    /// <param name="thread">The conversation the question was asked about.</param>
    /// <returns>The same scope restricted to that conversation.</returns>
    /// <remarks>
    /// It narrows and never widens: the accounts, the folders, and the readable set still apply, so a conversation
    /// reaching into a folder this caller may not read returns nothing from that folder.
    /// </remarks>
    public MailboxScope NarrowedToThread(EmailThreadId thread) => this with { SelectedThread = thread };

    /// <summary>Restricts the scope to individual emails, on top of everything already narrowing it.</summary>
    /// <param name="selectedEmails">The emails the question was asked about, at most <see cref="MaximumSelectedEmails" /> of them.</param>
    /// <returns>The same scope restricted to those emails, deduplicated and ordered.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">More emails were named than <see cref="MaximumSelectedEmails" /> admits.</exception>
    /// <remarks>
    /// The bound counts what the caller wrote rather than the distinct identifiers left afterwards, so a request cannot
    /// buy a larger predicate by repeating one. Ordering and deduplication are what make two requests naming the same
    /// selection in a different order one query with one cursor, exactly as the two lists above are ordered.
    /// </remarks>
    public MailboxScope NarrowedToEmails(IReadOnlyList<StoredEmailId> selectedEmails)
    {
        ArgumentNullException.ThrowIfNull(selectedEmails);
        MailboxQueryFilterInvalidException.ThrowIfCountExceeded(
            selectedEmails.Count,
            MaximumSelectedEmails,
            "selected emails");

        return this with
        {
            SelectedEmails = [.. selectedEmails.Distinct().OrderBy(static email => email.Value)],
        };
    }

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

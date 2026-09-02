// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads bounded pages of the local mailbox timeline out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every filter, the keyset boundary, the ordering, and the row limit are evaluated by PostgreSQL. Nothing is filtered
/// after materialization, so the page a caller receives costs one query over the timeline indexes rather than a scan
/// this process narrows afterwards.
/// </para>
/// <para>
/// The result is a projection, and the reason is privacy before performance: the query names the columns a listing
/// publishes, so no code path here can reach the stored raw MIME even by accident, and none of it enters the change
/// tracker.
/// </para>
/// <para>
/// A scope naming several accounts is read as a walk per account merged on one ordering rather than as one predicate
/// over an account list, and <see cref="MergedPage" /> says why. A scope naming one account keeps the query it has
/// always had, which is the ordered walk the merge is assembled out of.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailTimelineReader(MailFathomDbContext dbContext) : IStoredEmailTimelineReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await Page(dbContext.StoredEmails.AsNoTracking(), filter, continueAfter, limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToSummary())];
    }

    /// <summary>Composes the one query a page is read from, over a scope naming any number of accounts.</summary>
    /// <param name="emails">The stored emails to read, untracked.</param>
    /// <param name="filter">The validated filters, the scope, and the direction being read.</param>
    /// <param name="continueAfter">The boundary a previous page ended at, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">How many rows the page holds at most.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// A scope naming at most one account is one ordered walk, which is the query this read has always issued. The one
    /// account it names is passed to that walk rather than left to the scope's list, because the list would be composed
    /// as <c>= ANY</c> and that is the shape <see cref="MergedPage" /> exists to avoid; a single-element array scan key
    /// is no more an ordered path than a longer one. A scope naming no account at all admits nothing, so the walk it
    /// composes returns nothing whichever way the narrowing is written. Several accounts are walked one at a time and
    /// merged, for the reason <see cref="MergedPage" /> gives; each of those walks is this same single-account query,
    /// so the two shapes are one composition rather than two readings of a filter.
    /// </remarks>
    internal static IQueryable<StoredEmailSummaryRow> Page(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit)
    {
        var accountsInScope = filter.Selection.Scope.AccountIds;

        return accountsInScope.Count > 1
            ? MergedPage(emails, filter, continueAfter, limit, accountsInScope)
            : PageOf(
                emails,
                filter,
                continueAfter,
                limit,
                withinAccount: accountsInScope.Count is 1 ? accountsInScope[0] : null);
    }

    /// <summary>Orders the timeline the way the ordering contract defines, including where undated mail lands.</summary>
    /// <remarks>
    /// <para>
    /// The leading key is what places undated mail: last when the newest is read first, first when the oldest is. It is
    /// written as an ordering key because PostgreSQL's default under <c>DESC</c> is <c>NULLS FIRST</c> — the opposite of
    /// the contract — and EF Core publishes no way to state a null sort order in a query. The timeline indexes spell out
    /// <c>NULLS LAST</c>, so the two agree on the order; whether PostgreSQL can serve this expression from those indexes
    /// without a sort step is a query-plan question the integration suite answers, and the answer there is a matching
    /// expression index rather than a different order here.
    /// </para>
    /// <para>
    /// The identifier is an ordering key rather than a decoration: two emails a mail server recorded in the same instant
    /// would otherwise have no defined order between them, and a page boundary computed from an undefined order skips or
    /// repeats rows.
    /// </para>
    /// </remarks>
    internal static IOrderedQueryable<StoredEmailEntity> InTimelineOrder(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineDirection direction) =>
        direction is EmailTimelineDirection.NewestFirst
            ? emails
                .OrderBy(email => email.ReceivedAt == null)
                .ThenByDescending(email => email.ReceivedAt)
                .ThenByDescending(email => email.Id)
            : emails
                .OrderByDescending(email => email.ReceivedAt == null)
                .ThenBy(email => email.ReceivedAt)
                .ThenBy(email => email.Id);

    /// <summary>Keeps the emails that fall strictly beyond a page boundary in the direction being read.</summary>
    /// <remarks>
    /// <para>
    /// The four branches are the keyset comparison of <see cref="EmailTimelinePosition" /> written as SQL, and they
    /// exist because the boundary itself may be undated. Reading newest first, undated mail forms the tail: every
    /// undated email lies beyond a dated boundary, and a boundary that is itself undated leaves only the undated emails
    /// whose identifier sorts lower. Reading oldest first the same tail leads instead, so the two cases invert.
    /// </para>
    /// <para>
    /// The identifier comparison is evaluated by PostgreSQL as a <c>uuid</c> comparison, which is what the timeline
    /// index is ordered by. It therefore never has to agree with how the CLR happens to compare two
    /// <see cref="Guid" /> values.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailEntity> Beyond(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelinePosition? continueAfter,
        EmailTimelineDirection direction)
    {
        if (continueAfter is not { } boundary)
        {
            return emails;
        }

        var boundaryId = boundary.StoredEmailId.Value;

        return (direction, boundary.ReceivedAt) switch
        {
            (EmailTimelineDirection.NewestFirst, { } receivedAt) => emails.Where(email =>
                email.ReceivedAt == null
                || email.ReceivedAt < receivedAt
                || (email.ReceivedAt == receivedAt && email.Id < boundaryId)),
            (EmailTimelineDirection.NewestFirst, null) => emails.Where(email =>
                email.ReceivedAt == null && email.Id < boundaryId),
            (EmailTimelineDirection.OldestFirst, { } receivedAt) => emails.Where(email =>
                email.ReceivedAt != null
                && (email.ReceivedAt > receivedAt
                    || (email.ReceivedAt == receivedAt && email.Id > boundaryId))),
            _ => emails.Where(email =>
                email.ReceivedAt != null || email.Id > boundaryId),
        };
    }

    /// <summary>Composes one page over a scope naming several accounts, as a walk per account merged on one ordering.</summary>
    /// <remarks>
    /// <para>
    /// The timeline index leads with the account, so it serves one account as an ordered walk and a keyset page costs
    /// the page. Across several accounts that ordering is not something an array scan key preserves — PostgreSQL derives
    /// an ordered path from partitioning or from appended paths rather than from <c>= ANY</c> — so a single predicate
    /// over the account list is planned as a scan of everything that matched followed by a top-N sort, and the keyset
    /// walk stops being the bounded thing it was designed to be. A mailbox of tens of thousands of messages per account
    /// is where that stops being a detail.
    /// </para>
    /// <para>
    /// So each account is walked on its own index, bounded to the page size, and the walks are appended and ordered
    /// together. What the outer ordering then sorts is at most one page per account rather than everything that matched,
    /// so what it costs follows the number of accounts rather than the size of the mailbox. The ordering is the
    /// single-account one, expression for expression, so the page a merge returns is the page the same scope would have
    /// returned had one query been able to produce it, and a continuation cursor taken from its last row resumes
    /// contiguously.
    /// </para>
    /// <para>
    /// Each walk takes the whole page limit rather than a share of it, because the page may come entirely from one
    /// account: a share would silently truncate a mailbox whose mail is the newest of the set. What that costs is one
    /// branch and one page of rows per account in scope, and the merge states no ceiling of its own on how many that
    /// is. <see cref="MailboxScope.MaximumAccountIds" /> bounds what a request may *name*, and a request naming
    /// nothing resolves to every account its owner owns, so the branch count follows the deployment's configured
    /// account list rather than that limit. Capping it here is the wrong repair — a page assembled from some of the
    /// caller's accounts would be a page missing mail nobody could tell was missing — so a deployment serving one
    /// owner enough accounts for the fan-out to matter is a bound to place on the accounts rather than on the walk.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailSummaryRow> MergedPage(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        IReadOnlyList<MailAccountId> accountsInScope)
    {
        var merged = accountsInScope
            .Select(accountId => OrderedWalk(emails, filter, continueAfter, accountId).Take(limit))
            .Aggregate(static (walked, next) => walked.Concat(next));

        return InTimelineOrder(merged, filter.Direction).Select(StoredEmailSummaryRow.Projection).Take(limit);
    }

    /// <summary>Composes one page over a scope naming at most one account, as one ordered walk.</summary>
    private static IQueryable<StoredEmailSummaryRow> PageOf(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        int limit,
        MailAccountId? withinAccount) =>
        OrderedWalk(emails, filter, continueAfter, withinAccount)
            .Select(StoredEmailSummaryRow.Projection)
            .Take(limit);

    /// <summary>Narrows the emails to what a selection admits beyond the page boundary, in the order the timeline is read.</summary>
    /// <remarks>
    /// The rows are still emails here rather than the projection a page publishes, which is what the merge above needs:
    /// EF Core refuses a set operation whose operands have already been projected into a type of their own, and the
    /// refusal is a translation failure at run time rather than something the compiler reports. Ordering the append on
    /// the table's own columns is the better shape anyway — it is the same expression a single walk is ordered by,
    /// rather than a copy of it rewritten against projected names, which is what makes the merged page the page one
    /// query would have produced and a cursor taken from its last row resume contiguously.
    /// </remarks>
    private static IOrderedQueryable<StoredEmailEntity> OrderedWalk(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter,
        EmailTimelinePosition? continueAfter,
        MailAccountId? withinAccount) =>
        InTimelineOrder(
            Beyond(
                StoredEmailSelectionPredicate.Matching(emails, filter.Selection, withinAccount),
                continueAfter,
                filter.Direction),
            filter.Direction);
}

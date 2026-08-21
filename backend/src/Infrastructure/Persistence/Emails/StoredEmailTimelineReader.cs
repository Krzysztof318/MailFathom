// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
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

        var selected = Beyond(
            StoredEmailSelectionPredicate.Matching(dbContext.StoredEmails.AsNoTracking(), filter.Selection),
            continueAfter,
            filter.Direction);

        var rows = await InTimelineOrder(selected, filter.Direction)
            .Select(StoredEmailSummaryRow.Projection)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(row => row.ToSummary())];
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
}

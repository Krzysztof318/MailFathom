// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Answering;

/// <summary>Keeps the record of the questions this deployment answered from each account's mailbox, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The append uses the context enlisted in the caller's session; the page read uses the scoped context, because it joins
/// no transaction. The erasure uses neither: it is a set-based delete that composes with nothing a caller is holding,
/// and the emails an erased entry named go with it through the entry's own foreign key.
/// </para>
/// <para>
/// Nothing here updates a row. An entry states a run that already ended, so the record only ever grows and shrinks, and
/// the one uniqueness it enforces — one entry per run per account — is what makes a repeated append leave it as it was.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAnsweringAuditEntryStore(
    MailFathomDbContext readContext,
    MailAnsweringAuditTelemetry telemetry)
    : IMailAnsweringAuditEntryStore
{
    /// <inheritdoc />
    public async Task AppendAsync(
        IPersistenceSession session,
        MailAnsweringAuditEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entry);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var runId = entry.RunId.Value;
        var accountId = entry.AccountId.Value;

        // Looked up by the run and account rather than by the key, because a retried append generates a fresh key and
        // the thing that must not happen twice is an entry for one question asked of one mailbox. The change-tracker
        // pass is explicit for the reason the mutation record's own lookup makes it explicit: an append staged earlier
        // in this same uncommitted session would be invisible to a query.
        var existing = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.MailAnsweringAuditEntries,
            writeContext.MailAnsweringAuditEntries,
            candidate => candidate.RunId == runId && candidate.MailboxAccountId == accountId,
            cancellationToken);

        if (existing is not null)
        {
            return;
        }

        writeContext.MailAnsweringAuditEntries.Add(MailAnsweringAuditEntryMapping.ToEntity(entry));
    }

    /// <inheritdoc />
    public async Task<MailAnsweringAuditPage> ReadPageAsync(
        MailAnsweringAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var accountValue = query.AccountId.Value;

        var entities = await this.Filter(query)
            .Where(record => record.MailboxAccountId == accountValue)

            // The emails are the point of the entry, so they are loaded with it rather than left to a second read per
            // row. The page is bounded and so is what one run may retrieve, which is what keeps the join bounded too.
            .Include(record => record.Emails)
            .OrderByDescending(record => record.CompletedAt)
            .ThenByDescending(record => record.Id)

            // One more than the page holds, which is how the answer says whether a following page exists without a
            // second count query over the same filtered set.
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var pageEntities = entities.Take(query.PageSize).ToArray();
        var entries = new List<MailAnsweringAuditEntry>(pageEntities.Length);
        var unreadableCount = 0;

        foreach (var entity in pageEntities)
        {
            if (MailAnsweringAuditEntryMapping.TryToEntry(entity, out var entry))
            {
                entries.Add(entry);
            }
            else
            {
                unreadableCount++;
            }
        }

        if (unreadableCount > 0)
        {
            telemetry.RecordUnreadableEntries(query.AccountId, unreadableCount);
        }

        // The boundary is the last row read rather than the last entry presented, so a row this build cannot interpret
        // costs its own place in the page and nothing else: the walk neither stalls on it nor repeats the rows around it.
        return new MailAnsweringAuditPage(
            entries,
            entities.Length > query.PageSize && pageEntities.Length > 0
                ? MailAnsweringAuditCursor.After(
                    pageEntities[^1].CompletedAt,
                    MailAnsweringAuditEntryId.Create(pageEntities[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    public async Task<int> EraseCompletedBeforeAsync(
        MailAccountId accountId,
        DateTimeOffset completedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = accountId.Value;

        // The bounded set is read first and deleted by key, rather than bounding the delete itself: PostgreSQL has no
        // `DELETE ... LIMIT`, so a bound expressed on the delete either fails to translate or becomes a subquery whose
        // shape depends on the provider. Two statements over the same index cost one extra round trip and keep the
        // statement that takes row locks small enough to state.
        var expiringIds = await readContext.MailAnsweringAuditEntries
            .AsNoTracking()
            .Where(record => record.MailboxAccountId == accountValue && record.CompletedAt < completedBefore)
            .OrderBy(record => record.CompletedAt)
            .ThenBy(record => record.Id)
            .Take(limit)
            .Select(record => record.Id)
            .ToArrayAsync(cancellationToken);

        if (expiringIds.Length == 0)
        {
            return 0;
        }

        // The emails go with the entries rather than being deleted beside them, because the foreign key cascades. A
        // second statement here would be a second place the same rule was written.
        return await readContext.MailAnsweringAuditEntries
            .Where(record => expiringIds.Contains(record.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Applies the filters a query names, leaving the account and the ordering to the caller.</summary>
    private IQueryable<MailAnsweringAuditEntryEntity> Filter(MailAnsweringAuditQuery query)
    {
        var records = readContext.MailAnsweringAuditEntries.AsNoTracking();

        if (query.CompletedFrom is { } completedFrom)
        {
            records = records.Where(record => record.CompletedAt >= completedFrom);
        }

        if (query.CompletedBefore is { } completedBefore)
        {
            records = records.Where(record => record.CompletedAt < completedBefore);
        }

        // The keyset boundary is the pair the order is taken on, so an entry that ended in the same instant as the last
        // one of the previous page is served exactly once rather than skipped or repeated. The identifier comparison is
        // evaluated by PostgreSQL as a `uuid` comparison, which is what the index is ordered by, so it never has to
        // agree with how the CLR happens to compare two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryCompletedAt = cursor.CompletedAt;
            var boundaryId = cursor.EntryId.Value;

            records = records.Where(record => record.CompletedAt < boundaryCompletedAt
                || (record.CompletedAt == boundaryCompletedAt && record.Id < boundaryId));
        }

        return records;
    }
}

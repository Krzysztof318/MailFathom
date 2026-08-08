// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Keeps the audit trail of every change MailFathom made to a remote mailbox, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The append uses the context enlisted in the caller's session; the page read uses the scoped context, because it joins
/// no transaction. The erasure uses neither: it is a set-based delete that composes with nothing a caller is holding.
/// </para>
/// <para>
/// Nothing here updates a row. An entry states an ending that already happened, so the trail only ever grows and
/// shrinks, and the one uniqueness it enforces — one entry per mutation record — is what makes a repeated append leave
/// the trail as it was.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxMutationAuditEntryStore(
    MailFathomDbContext readContext,
    MailboxMutationAuditTelemetry telemetry)
    : IMailboxMutationAuditEntryStore
{
    /// <inheritdoc />
    public async Task AppendAsync(
        IPersistenceSession session,
        MailboxMutationAuditEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entry);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var mutationRecordId = entry.MutationRecordId.Value;

        // Looked up by the mutation rather than by the key, because a retried append generates a fresh key and the
        // thing that must not happen twice is an entry for one ending. The change-tracker pass is explicit for the
        // reason the mutation record's own lookup makes it explicit: an append staged earlier in this same uncommitted
        // session would be invisible to a query.
        var existing = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.MailboxMutationAuditEntries,
            writeContext.MailboxMutationAuditEntries,
            candidate => candidate.MutationRecordId == mutationRecordId,
            cancellationToken);

        if (existing is not null)
        {
            return;
        }

        writeContext.MailboxMutationAuditEntries.Add(MailboxMutationAuditEntryMapping.ToEntity(entry));
    }

    /// <inheritdoc />
    public async Task<MailboxMutationAuditPage> ReadPageAsync(
        MailboxMutationAuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var accountValue = query.AccountId.Value;

        var entities = await this.Filter(query)
            .Where(entry => entry.MailboxAccountId == accountValue)
            .OrderByDescending(entry => entry.CompletedAt)
            .ThenByDescending(entry => entry.Id)

            // One more than the page holds, which is how the answer says whether a following page exists without a
            // second count query over the same filtered set.
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var pageEntities = entities.Take(query.PageSize).ToArray();
        var entries = new List<MailboxMutationAuditEntry>(pageEntities.Length);
        var unreadableCount = 0;

        foreach (var entity in pageEntities)
        {
            if (MailboxMutationAuditEntryMapping.TryToEntry(entity, out var entry))
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
        return new MailboxMutationAuditPage(
            entries,
            entities.Length > query.PageSize && pageEntities.Length > 0
                ? MailboxMutationAuditCursor.After(
                    pageEntities[^1].CompletedAt,
                    MailboxMutationAuditEntryId.Create(pageEntities[^1].Id),
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
        var expiringIds = await readContext.MailboxMutationAuditEntries
            .AsNoTracking()
            .Where(entry => entry.MailboxAccountId == accountValue && entry.CompletedAt < completedBefore)
            .OrderBy(entry => entry.CompletedAt)
            .ThenBy(entry => entry.Id)
            .Take(limit)
            .Select(entry => entry.Id)
            .ToArrayAsync(cancellationToken);

        if (expiringIds.Length == 0)
        {
            return 0;
        }

        return await readContext.MailboxMutationAuditEntries
            .Where(entry => expiringIds.Contains(entry.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Applies the filters a query names, leaving the account and the ordering to the caller.</summary>
    /// <remarks>
    /// The mutation is compared by its stored name rather than through the closed enumeration, because a value object's
    /// member inside a translated lambda either fails to translate or forces client evaluation.
    /// </remarks>
    private IQueryable<MailboxMutationAuditEntryEntity> Filter(MailboxMutationAuditQuery query)
    {
        var entries = readContext.MailboxMutationAuditEntries.AsNoTracking();

        if (query.Mutation.IsSpecified)
        {
            var mutationName = query.Mutation.Name;
            entries = entries.Where(entry => entry.Mutation == mutationName);
        }

        if (query.CompletedFrom is { } completedFrom)
        {
            entries = entries.Where(entry => entry.CompletedAt >= completedFrom);
        }

        if (query.CompletedBefore is { } completedBefore)
        {
            entries = entries.Where(entry => entry.CompletedAt < completedBefore);
        }

        // The keyset boundary is the pair the order is taken on, so an entry that finished in the same instant as the
        // last one of the previous page is served exactly once rather than skipped or repeated. The identifier
        // comparison is evaluated by PostgreSQL as a `uuid` comparison, which is what the index is ordered by, so it
        // never has to agree with how the CLR happens to compare two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryCompletedAt = cursor.CompletedAt;
            var boundaryId = cursor.EntryId.Value;

            entries = entries.Where(entry => entry.CompletedAt < boundaryCompletedAt
                || (entry.CompletedAt == boundaryCompletedAt && entry.Id < boundaryId));
        }

        return entries;
    }
}

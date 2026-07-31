// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core state for the bounded backward pass that re-checks stored emails against their mail server.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so one window's observations and deletions commit or roll back together.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailReconciliationStore(MailMcpDbContext readContext) : IStoredEmailReconciliationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The two groups are read as two bounded queries rather than as one ordered scan, because the reservation the port
    /// describes is not expressible as an ordering: it has to stop newly stored mail from filling every window. Both
    /// queries are ordered and limited by PostgreSQL and neither can return more than the window holds.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        var eligible = this.EligibleEmails(accountId, folderResolutionId, uidValidity);

        // Read first so the never-observed budget can be computed from how many previously observed emails actually
        // exist: a folder that has none must still fill its whole window with new mail rather than leave it empty.
        var previouslyObserved = await eligible
            .Where(email => email.RemoteFlagsObservedAt != null)
            .OrderBy(email => email.RemoteFlagsObservedAt)
            .ThenBy(email => email.Uid)
            .Take(maxEmailCount)
            .Select(email => new { email.Id, email.Uid })
            .ToArrayAsync(cancellationToken);

        var neverObserved = await eligible
            .Where(email => email.RemoteFlagsObservedAt == null)
            .OrderBy(email => email.Uid)
            .Take(ReconciliationWindowBudget.NeverObservedShareOf(maxEmailCount, previouslyObserved.Length))
            .Select(email => new { email.Id, email.Uid })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. neverObserved
                .Concat(previouslyObserved.Take(maxEmailCount - neverObserved.Length))
                .Select(static candidate => new StoredEmailAwaitingReconciliation(
                    StoredEmailId.Create(candidate.Id),
                    ImapUid.Create(candidate.Uid))),
        ];
    }

    /// <inheritdoc />
    public async Task ApplyReconciliationOutcomeAsync(
        IPersistenceSession session,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var rowsById = await LoadWindowRowsAsync(sessionContext, outcome, cancellationToken);

        foreach (var observed in outcome.StillPresent)
        {
            if (rowsById.TryGetValue(observed.StoredEmailId.Value, out var row)
                && !HasNewerObservationThan(row, observed.Snapshot.ObservedAt ?? outcome.ObservedAt))
            {
                ApplyFlagSnapshot(row, observed.Snapshot);
            }
        }

        var disappeared = outcome.Disappeared
            .Select(storedEmailId => rowsById.GetValueOrDefault(storedEmailId.Value))
            .OfType<StoredEmailEntity>()
            .Where(row => !HasNewerObservationThan(row, outcome.ObservedAt))
            .ToArray();

        if (outcome.Disposition is RemotelyDeletedEmailDisposition.EraseLocalCopy)
        {
            // One RemoveRange rather than a remove per row: the raw MIME, the search document, and any outstanding
            // repair request are declared with OnDelete(Cascade) from this row, so PostgreSQL removes them too.
            sessionContext.StoredEmails.RemoveRange(disappeared);

            return;
        }

        foreach (var row in disappeared)
        {
            row.RemoteExpungeObservedAt ??= outcome.ObservedAt;

            // The tombstone leaves the reconciliation queue as well as the mailbox queries, and the observation
            // timestamp is what takes it out: a window is ordered by that column, so a row left never-observed would be
            // selected again on every run for an email the server will never answer about.
            row.RemoteFlagsObservedAt ??= outcome.ObservedAt;
        }
    }

    /// <summary>Narrows the stored emails to the ones one folder binding's window may select from.</summary>
    private IQueryable<StoredEmailEntity> EligibleEmails(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity)
    {
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        return readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailFolder.MailboxAccountId == accountId.Value
                && email.MailFolder.Alias == alias
                && email.MailFolder.ResolutionGeneration == generation
                && email.UidValidity == uidValidityValue
                && email.RemoteExpungeObservedAt == null);
    }

    /// <summary>Reads every row the outcome names in one query, so applying a window costs one round trip rather than one per email.</summary>
    /// <remarks>
    /// The rows are tracked on purpose: this is the write path, and the whole point of loading them together is that
    /// the updates and removals below are staged against entities the session already holds. A row the query does not
    /// return was removed by another writer, which the callers above treat as nothing left to do.
    /// </remarks>
    private static async Task<Dictionary<Guid, StoredEmailEntity>> LoadWindowRowsAsync(
        MailMcpDbContext sessionContext,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        var windowIds = outcome.StillPresent
            .Select(static observed => observed.StoredEmailId.Value)
            .Concat(outcome.Disappeared.Select(static storedEmailId => storedEmailId.Value))
            .ToArray();

        var rows = await sessionContext.StoredEmails
            .Where(email => windowIds.Contains(email.Id))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(static row => row.Id);
    }

    /// <summary>Reports whether another writer has already recorded an observation at least as recent as this one.</summary>
    /// <remarks>
    /// This is what makes a window safe to replay after a commit conflict, and it is a correctness rule rather than an
    /// optimization. A retried window carries the answer a server gave before the conflict, so applying it
    /// unconditionally could move the queue timestamp backwards, overwrite fresher flags, or — worst of the three —
    /// delete an email that a later run has since proved the server still holds.
    /// </remarks>
    private static bool HasNewerObservationThan(StoredEmailEntity row, DateTimeOffset observedAt) =>
        row.RemoteFlagsObservedAt is { } recordedAt && recordedAt > observedAt;

    private static void ApplyFlagSnapshot(StoredEmailEntity row, RemoteEmailFlagSnapshot snapshot)
    {
        row.RemoteFlagsObservedAt = snapshot.ObservedAt;
        row.IsRemotelySeen = snapshot.IsSeen;
        row.IsRemotelyAnswered = snapshot.IsAnswered;
        row.IsRemotelyFlagged = snapshot.IsFlagged;
        row.IsRemotelyDraft = snapshot.IsDraft;
        row.IsRemotelyDeleted = snapshot.IsDeleted;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core state for the bounded backward pass that re-checks stored emails against their mail server.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so one window's observations and deletions commit or roll back together.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailReconciliationStore(MailFathomDbContext readContext) : IStoredEmailReconciliationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The two groups are read as two bounded queries rather than as one ordered scan, because the reservation the port
    /// describes is not expressible as an ordering: it has to stop newly stored mail from filling every window. Both
    /// queries are ordered and limited by PostgreSQL and neither can return more than the window holds.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetReconciliationWindowAsync(
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        var eligible = this.EligibleEmails(account, folderResolutionId, uidValidity);

        // Read first so the never-observed budget can be computed from how many previously observed emails actually
        // exist: a folder that has none must still fill its whole window with new mail rather than leave it empty.
        // The previous reading of the values a mutation may write travels with the candidate because such a change is a
        // comparison rather than a reading: the server reports what stands now, and only the earlier values and the
        // moment they were taken say whether anybody moved one and whether a change MailFathom made could still be why.
        var previouslyObserved = await eligible
            .Where(email => email.RemoteFlagsObservedAt != null)
            .OrderBy(email => email.RemoteFlagsObservedAt)
            .ThenBy(email => email.Uid)
            .Take(maxEmailCount)
            .Select(email => new
            {
                email.Id,
                Uid = email.Uid!.Value,
                ObservedAt = email.RemoteFlagsObservedAt,
                email.IsRemotelySeen,
                email.IsRemotelyFlagged,
                email.RemoteKeywords,
            })
            .ToArrayAsync(cancellationToken);

        var neverObserved = await eligible
            .Where(email => email.RemoteFlagsObservedAt == null)
            .OrderBy(email => email.Uid)
            .Take(ReconciliationWindowBudget.NeverObservedShareOf(maxEmailCount, previouslyObserved.Length))
            .Select(email => new
            {
                email.Id,
                Uid = email.Uid!.Value,
                ObservedAt = (DateTimeOffset?)null,
                email.IsRemotelySeen,
                email.IsRemotelyFlagged,
                email.RemoteKeywords,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. neverObserved
                .Concat(previouslyObserved.Take(maxEmailCount - neverObserved.Length))
                .Select(static candidate => new StoredEmailAwaitingReconciliation(
                    StoredEmailId.Create(candidate.Id),
                    ImapUid.Create(candidate.Uid),
                    candidate.ObservedAt is { } observedAt
                        ? new RemoteWritableFlagObservation(
                            observedAt,
                            candidate.IsRemotelySeen,
                            candidate.IsRemotelyFlagged,
                            RemoteEmailKeywords.Create(candidate.RemoteKeywords))
                        : null)),
        ];
    }

    /// <inheritdoc />
    public async Task ApplyReconciliationOutcomeAsync(
        IPersistenceSession session,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var rowsById = await LoadWindowRowsAsync(sessionContext, outcome, cancellationToken);

        foreach (var observed in outcome.StillPresent)
        {
            if (rowsById.TryGetValue(observed.StoredEmailId.Value, out var row)
                && !HasNewerObservationThan(row, observed.Snapshot.ObservedAt ?? outcome.ObservedAt))
            {
                ApplyFlagSnapshot(row, observed.Snapshot);
            }
        }

        // A confirmed email keeps its stored flags and moves only its place in the queue, because the server's answer
        // was that nothing about it has changed. Writing the flag columns from the stored values instead would be the
        // same row with a fresher-looking observation and one more chance to get the copy wrong.
        foreach (var confirmed in outcome.ConfirmedUnchanged)
        {
            if (rowsById.TryGetValue(confirmed.Value, out var row)
                && !HasNewerObservationThan(row, outcome.ObservedAt))
            {
                row.RemoteFlagsObservedAt = outcome.ObservedAt;
            }
        }

        // A disappearance MailFathom itself caused is not the remote deletion the disposition below answers for, so it
        // never reaches that setting. A relocation into a mirrored folder moves the queue timestamp and nothing else,
        // because the row is on its way into that folder; a delete, and a relocation that carried the message out of the
        // mirrored mailbox altogether, additionally apply the disposition their own record carries, which is the one the
        // user authored the change under rather than whatever the account is configured with by now.
        var erasedByAuthoredDelete = new List<StoredEmailEntity>();

        foreach (var attributed in outcome.RemovedByOwnMutation)
        {
            if (!rowsById.TryGetValue(attributed.StoredEmailId.Value, out var row)
                || HasNewerObservationThan(row, outcome.ObservedAt))
            {
                continue;
            }

            row.RemoteFlagsObservedAt = outcome.ObservedAt;

            // Absent for a relocation whose destination MailFathom mirrors, which is the request's own invariant: such a
            // relocation has its row carried into the destination folder by the placement instead.
            if (attributed.LocalDisposition is not { } authoredDisposition)
            {
                continue;
            }

            if (authoredDisposition is AuthoredDeleteEmailDisposition.EraseLocalCopy)
            {
                erasedByAuthoredDelete.Add(row);

                continue;
            }

            // Both retaining values record the expunge, because the server genuinely no longer holds the message and
            // the reconciliation queue has to stop selecting a row nothing will ever answer about again. Which of the
            // two it was decides only whether mailbox queries still admit the row.
            row.RemoteExpungeObservedAt ??= outcome.ObservedAt;
            row.IsRetainedAfterAuthoredDelete =
                authoredDisposition is AuthoredDeleteEmailDisposition.RetainLocalCopy;
        }

        await EraseAuthoredDeletesAsync(session, sessionContext, erasedByAuthoredDelete, cancellationToken);

        var disappeared = outcome.Disappeared
            .Select(storedEmailId => rowsById.GetValueOrDefault(storedEmailId.Value))
            .OfType<StoredEmailEntity>()
            .Where(row => !HasNewerObservationThan(row, outcome.ObservedAt))
            .ToArray();

        if (!outcome.AppliesRemoteDeletions)
        {
            // The account's mailbox is MailFathom's to keep, so a message gone from the source is the drain's own work
            // completing rather than somebody deleting mail. The row keeps its whole life and loses only the pointer at
            // a position no server holds any more, which is the same clearing the drain would have written itself.
            foreach (var row in disappeared)
            {
                row.UidValidity = null;
                row.Uid = null;
            }

            return;
        }

        if (outcome.Disposition is RemotelyDeletedEmailDisposition.EraseLocalCopy)
        {
            // The cascade takes the raw MIME with the row, and nothing below the content store observes that cascade,
            // so the user's stored-content figure has to give those bytes back explicitly or it would go on bounding
            // a user against payloads that are gone. It reads the lengths itself, which is why it belongs before this
            // session commits rather than at any particular point among the staged removals below.
            await AccountStoredContentLedger.RemoveAsync(
                sessionContext,
                [.. disappeared.Select(email => email.Id)],
                cancellationToken);

            // Read first for the same reason: the cascade below takes the only pointer to an object with it, and this
            // disposition exists to erase the local copy rather than to keep it.
            await ReleasedContentObjects.ReleaseForStoredEmailsAsync(
                session,
                [.. disappeared.Select(static row => row.Id)],
                cancellationToken);

            // One RemoveRange rather than a remove per row: the raw MIME, the search document, the chunks and their
            // vectors, and any outstanding repair request are declared with OnDelete(Cascade) from this row, so
            // PostgreSQL removes them too.
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeleteLeftFlagged>> GetDeletesLeftFlaggedAsync(
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var accountValue = account.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        // ponytail: an occurrence recorded under a UIDVALIDITY the folder no longer reports is never asked about again and
        // is left in place; it names a position and nothing of the message. Remove such rows once they are ever counted.
        var followed = await readContext.MailboxFlaggedDeletes
            .AsNoTracking()
            .Where(flagged => flagged.MailboxAccountId == accountValue
                && readContext.MailFolders.Any(folder => folder.Id == flagged.MailFolderId
                    && folder.Alias == alias
                    && folder.ResolutionGeneration == generation)
                && flagged.UidValidity == uidValidityValue)
            .OrderBy(flagged => flagged.LastObservedAt)
            .ThenBy(flagged => flagged.Id)
            .Take(maxCount)
            .Select(flagged => new { flagged.Id, flagged.Uid, flagged.StoredEmailId })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. followed.Select(static flagged => new DeleteLeftFlagged(
                new DeleteLeftFlaggedId(flagged.Id),
                ImapUid.Create(flagged.Uid),
                flagged.StoredEmailId is { } kept ? StoredEmailId.Create(kept) : null)),
        ];
    }

    /// <inheritdoc />
    public async Task ApplyFlaggedDeleteSettlementAsync(
        IPersistenceSession session,
        FlaggedDeleteSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settlement);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var observedAt = settlement.ObservedAt;
        Guid[] rowIds =
        [
            .. settlement.Settled.Select(static settled => settled.StoredEmailId.Value),
            .. settlement.Expunged.Concat(settlement.Restored)
                .Select(static entry => entry.KeptEmail?.Value)
                .OfType<Guid>(),
        ];
        var rowsById = await sessionContext.StoredEmails
            .Where(email => rowIds.Contains(email.Id))
            .ToDictionaryAsync(static email => email.Id, cancellationToken);

        var settledRows = settlement.Settled
            .Where(settled => rowsById.ContainsKey(settled.StoredEmailId.Value))
            .Select(settled => (Row: rowsById[settled.StoredEmailId.Value], settled.LocalDisposition))
            .Where(static settled => settled.Row.UidValidity is not null
                && settled.Row.Uid is not null
                && settled.Row.AuthoredDeleteFlaggedAt is null)
            .DistinctBy(static settled => settled.Row.Id)
            .ToArray();

        foreach (var (row, disposition) in settledRows)
        {
            sessionContext.MailboxFlaggedDeletes.Add(new MailboxFlaggedDeleteEntity
            {
                Id = Guid.CreateVersion7(observedAt),
                MailboxAccountId = row.MailboxAccountId,
                MailFolderId = row.MailFolderId,
                UidValidity = row.UidValidity!.Value,
                Uid = row.Uid!.Value,
                StoredEmailId = disposition is AuthoredDeleteEmailDisposition.EraseLocalCopy ? null : row.Id,
                LastObservedAt = observedAt,
            });

            if (disposition is AuthoredDeleteEmailDisposition.EraseLocalCopy)
            {
                continue;
            }

            row.AuthoredDeleteFlaggedAt ??= observedAt;
            row.IsRetainedAfterAuthoredDelete = disposition is AuthoredDeleteEmailDisposition.RetainLocalCopy;
            row.IsRemotelyDeleted = true;
            row.RemoteFlagsObservedAt = observedAt;
        }

        await EraseAuthoredDeletesAsync(
            session,
            sessionContext,
            [
                .. settledRows
                    .Where(static settled => settled.LocalDisposition is AuthoredDeleteEmailDisposition.EraseLocalCopy)
                    .Select(static settled => settled.Row),
            ],
            cancellationToken);

        // The row stays exactly as the delete left it — readable or a tombstone — and only records what the server did.
        foreach (var row in KeptRowsOf(settlement.Expunged, rowsById))
        {
            row.RemoteExpungeObservedAt ??= observedAt;
        }

        // A never-observed reading is what puts the row back in the backward pass, whose next window then reads its flags
        // as a first observation rather than as a change somebody made.
        foreach (var row in KeptRowsOf(settlement.Restored, rowsById))
        {
            row.AuthoredDeleteFlaggedAt = null;
            row.IsRetainedAfterAuthoredDelete = false;
            row.IsRemotelyDeleted = false;
            row.RemoteFlagsObservedAt = null;
        }

        Guid[] stillFlaggedIds = [.. settlement.StillFlagged.Select(static entry => entry.Id.Value)];

        if (stillFlaggedIds.Length > 0)
        {
            await sessionContext.MailboxFlaggedDeletes
                .Where(flagged => stillFlaggedIds.Contains(flagged.Id) && flagged.LastObservedAt < observedAt)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(flagged => flagged.LastObservedAt, observedAt),
                    cancellationToken);
        }

        await this.RetireDeletesLeftFlaggedAsync(
            session,
            [.. settlement.Expunged.Concat(settlement.Restored).Select(static entry => entry.Id)],
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RetireDeletesLeftFlaggedAsync(
        IPersistenceSession session,
        IReadOnlyList<DeleteLeftFlaggedId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return;
        }

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        Guid[] retiredIds = [.. ids.Select(static id => id.Value)];

        await sessionContext.MailboxFlaggedDeletes
            .Where(flagged => retiredIds.Contains(flagged.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Removes the rows an authored delete said to erase, with everything that leaves storage with them.</summary>
    private static async Task EraseAuthoredDeletesAsync(
        IPersistenceSession session,
        MailFathomDbContext sessionContext,
        List<StoredEmailEntity> erased,
        CancellationToken cancellationToken)
    {
        if (erased.Count == 0)
        {
            return;
        }

        // What these messages hold leaves storage with them, so their user's figure gives it back inside the same
        // transaction. What it subtracts is read from the payloads, so the constraint is that it runs before this
        // session commits rather than before the line below it: the removal below only stages a delete the change
        // tracker applies at that commit. A later change making the removal set-based would execute immediately and
        // turn that ordering into a real one.
        await AccountStoredContentLedger.RemoveAsync(
            sessionContext,
            [.. erased.Select(email => email.Id)],
            cancellationToken);

        // An authored delete is a deliberate erasure, so it carries through to the object rather than being left
        // to the sweep: the keys are read here, before the cascade removes the rows that carry them.
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(
            session,
            [.. erased.Select(static row => row.Id)],
            cancellationToken);

        sessionContext.StoredEmails.RemoveRange(erased);
    }

    /// <summary>Finds the rows a followed occurrence kept, skipping one another writer has already removed.</summary>
    private static IEnumerable<StoredEmailEntity> KeptRowsOf(
        IReadOnlyList<DeleteLeftFlagged> entries,
        Dictionary<Guid, StoredEmailEntity> rowsById) =>
        entries
            .Select(entry => entry.KeptEmail is { } kept ? rowsById.GetValueOrDefault(kept.Value) : null)
            .OfType<StoredEmailEntity>();

    /// <summary>Narrows the stored emails to the ones one folder binding's window may select from.</summary>
    private IQueryable<StoredEmailEntity> EligibleEmails(
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity)
    {
        var accountValue = account.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        return readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailFolder.MailboxAccountId == accountValue
                && email.MailFolder.Alias == alias
                && email.MailFolder.ResolutionGeneration == generation
                && email.UidValidity == uidValidityValue
                && email.RemoteExpungeObservedAt == null
                && email.AuthoredDeleteFlaggedAt == null);
    }

    /// <summary>Reads every row the outcome names in one query, so applying a window costs one round trip rather than one per email.</summary>
    /// <remarks>
    /// The rows are tracked on purpose: this is the write path, and the whole point of loading them together is that
    /// the updates and removals below are staged against entities the session already holds. A row the query does not
    /// return was removed by another writer, which the callers above treat as nothing left to do.
    /// </remarks>
    private static async Task<Dictionary<Guid, StoredEmailEntity>> LoadWindowRowsAsync(
        MailFathomDbContext sessionContext,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        var windowIds = outcome.StillPresent
            .Select(static observed => observed.StoredEmailId.Value)
            .Concat(outcome.ConfirmedUnchanged.Select(static storedEmailId => storedEmailId.Value))
            .Concat(outcome.Disappeared.Select(static storedEmailId => storedEmailId.Value))
            .Concat(outcome.RemovedByOwnMutation.Select(static attributed => attributed.StoredEmailId.Value))
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
        row.RemoteKeywords = [.. snapshot.Keywords.Values];
    }
}

// Copyright © 2026 Krzysztof Kasprowicz

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
/// The read path uses the scoped context because it joins no transaction. Both write paths use the context enlisted in
/// the caller's session, so one window's observations and deletions commit or roll back together.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailReconciliationStore(MailMcpDbContext readContext) : IStoredEmailReconciliationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The ordering is the one <see cref="MailMcpDbContext.StoredEmailReconciliationQueueIndexName" /> is declared with,
    /// so a window is an index scan of its first rows rather than a sort over the folder. The leading key is what places
    /// the never-observed emails first: PostgreSQL orders nulls last under <c>ASC</c>, which is the opposite of the
    /// decision, and EF Core publishes no way to state a null sort order in a query.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingReconciliation>> GetLeastRecentlyObservedAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEmailCount);

        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        var candidates = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailFolder.MailboxAccountId == accountId.Value
                && email.MailFolder.Alias == alias
                && email.MailFolder.ResolutionGeneration == generation
                && email.UidValidity == uidValidityValue
                && email.RemoteExpungeObservedAt == null)
            .OrderBy(email => email.RemoteFlagsObservedAt != null)
            .ThenBy(email => email.RemoteFlagsObservedAt)
            .ThenBy(email => email.Uid)
            .Take(maxEmailCount)
            .Select(email => new { email.Id, email.Uid })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingReconciliation(
                StoredEmailId.Create(candidate.Id),
                ImapUid.Create(candidate.Uid))),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// A row that no longer exists is nothing to observe rather than a failure. Reconciliation is the operation that
    /// removes rows, so a competing writer erasing the same email between this window's query and its commit is an
    /// ordinary race whose outcome both writers agree on; faulting the folder's run over it would only defer the same
    /// answer to the next interval.
    /// </remarks>
    public async Task RecordFlagObservationAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailFlagSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken);

        if (storedEmail is null)
        {
            return;
        }

        storedEmail.RemoteFlagsObservedAt = snapshot.ObservedAt;
        storedEmail.IsRemotelySeen = snapshot.IsSeen;
        storedEmail.IsRemotelyAnswered = snapshot.IsAnswered;
        storedEmail.IsRemotelyFlagged = snapshot.IsFlagged;
        storedEmail.IsRemotelyDraft = snapshot.IsDraft;
        storedEmail.IsRemotelyDeleted = snapshot.IsDeleted;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A row that is already gone is not an error to remove again, and a row that already carries an expunge timestamp
    /// keeps it, so replaying a window commits the same state it would have committed the first time. Removing the row
    /// takes the raw MIME, the search document, and any outstanding repair request with it: all three are declared with
    /// <c>OnDelete(Cascade)</c> from this row, so PostgreSQL removes them in the same statement rather than leaving
    /// derived data behind the message it was derived from.
    /// </remarks>
    public async Task RecordRemoteDeletionAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemotelyDeletedEmailDisposition disposition,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken);

        if (storedEmail is null)
        {
            return;
        }

        if (disposition is RemotelyDeletedEmailDisposition.EraseLocalCopy)
        {
            sessionContext.StoredEmails.Remove(storedEmail);

            return;
        }

        storedEmail.RemoteExpungeObservedAt ??= observedAt;

        // The tombstone leaves the reconciliation queue as well as the mailbox queries, and the observation timestamp is
        // what takes it out: a window is ordered by that column, so a row left never-observed would be selected again on
        // every run for an email the server will never answer about.
        storedEmail.RemoteFlagsObservedAt ??= observedAt;
    }
}

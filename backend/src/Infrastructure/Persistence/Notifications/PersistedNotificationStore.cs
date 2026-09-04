// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Notifications;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Notifications;

/// <summary>Keeps what happened to a person, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The raise opens a session of its own rather than joining a caller's, because no producer has one: what raises a
/// notification is a worker reporting on a run it has already committed, and composing the report into that run's
/// transaction would make a notification able to fail mail that was already stored.
/// </para>
/// <para>
/// It runs under the ordinary commit policy, so the read that decides whether the condition is already standing and
/// the insert that follows it are one transaction, and a writer that got there first is a retry rather than a failure:
/// the replay re-reads, finds the winner's unread row, and raises nothing. The partial unique index is what makes that
/// sound instead of merely likely — the read on its own would see nothing on both sides of a race.
/// </para>
/// <para>
/// The erasure uses neither: it is a set-based delete that composes with nothing a caller is holding.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedNotificationStore(
    MailFathomDbContext readContext,
    OptimisticConcurrencyRetryPolicy commitPolicy)
    : INotificationStore
{
    /// <inheritdoc />
    public Task<bool> RecordAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return commitPolicy.CommitAsync(
            (session, token) => StageAsync(session, notification, token),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read joins no transaction and takes no session, so it runs on the scoped context. The predicate walks the
    /// timeline index the model declares — one owner's rows, newest first, with the identifier breaking a tie — which
    /// is what makes page four hundred cost what page one costs.
    /// </remarks>
    public async Task<IReadOnlyList<Notification>> ReadPageAsync(
        MailOwnerId owner,
        NotificationCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var ownerValue = owner.Value;
        var rows = readContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.OwnerId == ownerValue);

        if (after is { } boundary)
        {
            var boundaryOccurredAt = boundary.OccurredAt;
            var boundaryId = boundary.NotificationId.Value;

            rows = rows.Where(notification => notification.OccurredAt < boundaryOccurredAt
                || (notification.OccurredAt == boundaryOccurredAt && notification.Id < boundaryId));
        }

        var page = await rows
            .OrderByDescending(notification => notification.OccurredAt)
            .ThenByDescending(notification => notification.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. page.Select(NotificationMapping.ToNotification)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// It is answered from the partial unique index the deduplication rule already declares, whose rows are exactly one
    /// owner's unread notifications, so the badge a client polls for costs an index-only count rather than a scan.
    /// </remarks>
    public Task<int> CountUnreadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        var ownerValue = owner.Value;

        return readContext.Notifications
            .AsNoTracking()
            .CountAsync(notification => notification.OwnerId == ownerValue && !notification.IsRead, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// It runs under the ordinary commit policy because the read that finds the row and the write that moves it are one
    /// decision, and because marking one unread has a second row to consider: the deduplication rule holds one unread
    /// notification per condition, so a condition said again while this one was read already stands unread in its
    /// place. That collision is answered here rather than left to the constraint, which would leave the policy
    /// retrying a write no replay can make succeed — the read is inside the transaction, so a writer that got there
    /// first is seen by the replay rather than by the index.
    /// </para>
    /// <para>
    /// The owner is part of the lookup rather than a check after it, which is what makes another owner's notification
    /// answer as one that does not exist.
    /// </para>
    /// </remarks>
    public Task<NotificationReadOutcome> SetReadAsync(
        MailOwnerId owner,
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken) =>
        commitPolicy.CommitAsync(
            (session, token) => StageReadStateAsync(session, owner, notification, isRead, token),
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A set-based update that composes with nothing a caller is holding, exactly as the erasure below is, and one that
    /// cannot collide with the deduplication rule: every row it touches leaves the partial index rather than joining it.
    /// </remarks>
    public Task<int> MarkAllReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        var ownerValue = owner.Value;

        return readContext.Notifications
            .Where(notification => notification.OwnerId == ownerValue && !notification.IsRead)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(notification => notification.IsRead, true),
                cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The bounded set is read first and deleted by key, rather than bounding the delete itself, for the reason every
    /// retention sweep here is written that way: PostgreSQL has no <c>DELETE ... LIMIT</c>, so a bound expressed on the
    /// delete either fails to translate or becomes a subquery whose shape depends on the provider.
    /// </remarks>
    public async Task<int> EraseOccurredBeforeAsync(
        MailOwnerId owner,
        DateTimeOffset occurredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var ownerValue = owner.Value;

        var expiringIds = await readContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.OwnerId == ownerValue && notification.OccurredAt < occurredBefore)
            .OrderBy(notification => notification.OccurredAt)
            .ThenBy(notification => notification.Id)
            .Take(limit)
            .Select(notification => notification.Id)
            .ToArrayAsync(cancellationToken);

        if (expiringIds.Length == 0)
        {
            return 0;
        }

        return await readContext.Notifications
            .Where(notification => expiringIds.Contains(notification.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task<NotificationReadOutcome> StageReadStateAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = owner.Value;
        var identifier = notification.Value;

        var stored = await writeContext.Notifications.FirstOrDefaultAsync(
            candidate => candidate.OwnerId == ownerValue && candidate.Id == identifier,
            cancellationToken);

        if (stored is null)
        {
            return NotificationReadOutcome.NotFound;
        }

        if (stored.IsRead == isRead)
        {
            return NotificationReadOutcome.Applied;
        }

        if (!isRead)
        {
            var condition = stored.DeduplicationKey;
            var alreadyStanding = await writeContext.Notifications.AnyAsync(
                candidate => candidate.OwnerId == ownerValue
                    && candidate.DeduplicationKey == condition
                    && !candidate.IsRead,
                cancellationToken);

            if (alreadyStanding)
            {
                return NotificationReadOutcome.ConditionAlreadyStanding;
            }
        }

        stored.IsRead = isRead;

        return NotificationReadOutcome.Applied;
    }

    private static async Task<bool> StageAsync(
        IPersistenceSession session,
        Notification notification,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = notification.Owner.Value;
        var deduplicationKey = notification.DeduplicationKey.Value;

        // The change-tracker pass is explicit for the reason every alternate-key lookup here makes it explicit: a raise
        // staged earlier in this same uncommitted session would be invisible to a query.
        var standing = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.Notifications,
            writeContext.Notifications,
            candidate => candidate.OwnerId == ownerValue
                && candidate.DeduplicationKey == deduplicationKey
                && !candidate.IsRead,
            cancellationToken);

        if (standing is not null)
        {
            return false;
        }

        writeContext.Notifications.Add(NotificationMapping.ToEntity(notification));

        return true;
    }
}

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

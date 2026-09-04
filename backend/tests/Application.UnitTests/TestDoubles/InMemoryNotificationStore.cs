// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what a producer recorded, under the same deduplication rule the persisted store publishes.</summary>
/// <remarks>
/// The rule is restated here rather than substituted away because it is what the producers are written against: a
/// producer that composed a fresh key per call would suppress nothing, and a test whose double accepted everything
/// would pass anyway. What it deliberately does not model is the race the partial unique index settles, which no
/// in-memory double can establish and the integration suite proves against a real database.
/// </remarks>
internal sealed class InMemoryNotificationStore : INotificationStore
{
    private readonly List<Notification> recorded = [];

    /// <summary>Gets the notifications this store kept, in the order they were recorded.</summary>
    public IReadOnlyList<Notification> Recorded => this.recorded;

    /// <inheritdoc />
    public Task<bool> RecordAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var standing = this.recorded.Any(candidate => candidate.Owner == notification.Owner
            && candidate.DeduplicationKey == notification.DeduplicationKey
            && !candidate.IsRead);

        if (standing)
        {
            return Task.FromResult(false);
        }

        this.recorded.Add(notification);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Notification>> ReadPageAsync(
        MailOwnerId owner,
        NotificationCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var ordered = this.recorded
            .Where(candidate => candidate.Owner == owner)
            .OrderByDescending(candidate => candidate.OccurredAt)
            .ThenByDescending(candidate => candidate.Id.Value);

        var beyond = after is { } boundary
            ? ordered.Where(candidate => candidate.OccurredAt < boundary.OccurredAt
                || (candidate.OccurredAt == boundary.OccurredAt
                    && candidate.Id.Value.CompareTo(boundary.NotificationId.Value) < 0))
            : ordered;

        return Task.FromResult<IReadOnlyList<Notification>>([.. beyond.Take(limit)]);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(MailOwnerId owner, CancellationToken cancellationToken) =>
        Task.FromResult(this.recorded.Count(candidate => candidate.Owner == owner && !candidate.IsRead));

    /// <inheritdoc />
    public Task<NotificationReadOutcome> SetReadAsync(
        MailOwnerId owner,
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken)
    {
        var position = this.recorded.FindIndex(candidate => candidate.Owner == owner && candidate.Id == notification);

        if (position < 0)
        {
            return Task.FromResult(NotificationReadOutcome.NotFound);
        }

        var stored = this.recorded[position];

        if (stored.IsRead == isRead)
        {
            return Task.FromResult(NotificationReadOutcome.Applied);
        }

        if (!isRead
            && this.recorded.Any(candidate => candidate.Owner == owner
                && candidate.DeduplicationKey == stored.DeduplicationKey
                && !candidate.IsRead))
        {
            return Task.FromResult(NotificationReadOutcome.ConditionAlreadyStanding);
        }

        this.recorded[position] = InReadState(stored, isRead);

        return Task.FromResult(NotificationReadOutcome.Applied);
    }

    /// <inheritdoc />
    public Task<int> MarkAllReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        var marked = 0;

        for (var position = 0; position < this.recorded.Count; position++)
        {
            if (this.recorded[position] is { IsRead: false } unread && unread.Owner == owner)
            {
                this.recorded[position] = InReadState(unread, isRead: true);
                marked++;
            }
        }

        return Task.FromResult(marked);
    }

    /// <summary>Rebuilds one notification in a stated read state, which is the only field a store may move.</summary>
    private static Notification InReadState(Notification notification, bool isRead) => Notification.Restore(
        notification.Id,
        notification.Owner,
        notification.Kind,
        notification.Title,
        notification.Body,
        notification.Source,
        notification.Target,
        notification.DeduplicationKey,
        notification.OccurredAt,
        isRead);

    /// <inheritdoc />
    public Task<int> EraseOccurredBeforeAsync(
        MailOwnerId owner,
        DateTimeOffset occurredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        Notification[] expiring =
        [
            .. this.recorded
                .Where(candidate => candidate.Owner == owner && candidate.OccurredAt < occurredBefore)
                .OrderBy(candidate => candidate.OccurredAt)
                .Take(limit),
        ];

        foreach (var notification in expiring)
        {
            this.recorded.Remove(notification);
        }

        return Task.FromResult(expiring.Length);
    }
}

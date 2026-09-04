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

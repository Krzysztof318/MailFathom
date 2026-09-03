// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Keeps what happened to a person while nobody was looking at their screen.</summary>
/// <remarks>
/// Every producer raises through this port rather than writing to the table, which is what makes the deduplication
/// rule one rule: a stage that composed its own insert would decide for itself whether a condition had already been
/// said, and two producers deciding that separately is how a notification centre starts repeating itself.
/// </remarks>
public interface INotificationStore
{
    /// <summary>Records one notification unless the condition it names is already standing unread.</summary>
    /// <param name="notification">The notification to keep.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the notification was kept, and <see langword="false" /> when an unread one already names the same condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The comparison is against the owner's unread notifications alone. A condition the person has already read is
    /// free to be said again, which is what keeps a credential that is refused, repaired, and refused again from being
    /// suppressed forever by the first statement about it.
    /// </remarks>
    Task<bool> RecordAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>Erases up to a bounded number of one owner's notifications that describe something older than a given instant.</summary>
    /// <param name="owner">The owner whose notifications are aged.</param>
    /// <param name="occurredBefore">The instant a notification must describe something older than to be erased.</param>
    /// <param name="limit">The greatest number of notifications one call may erase.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many notifications were erased, which reaching <paramref name="limit" /> means more remain.</returns>
    /// <remarks>
    /// It erases read and unread alike: the bound is a storage-limitation decision about a derived record, not a
    /// reading list, and a statement nobody opened in three months is not one they are about to.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    Task<int> EraseOccurredBeforeAsync(
        MailOwnerId owner,
        DateTimeOffset occurredBefore,
        int limit,
        CancellationToken cancellationToken);
}

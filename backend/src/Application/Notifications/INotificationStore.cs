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

    /// <summary>Reads one bounded page of an owner's notifications, newest first.</summary>
    /// <param name="owner">The owner whose notifications are read.</param>
    /// <param name="after">The boundary a continued walk reads beyond, or <see langword="null" /> for the newest page.</param>
    /// <param name="limit">The greatest number of notifications the page may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, newest first, holding at most <paramref name="limit" /> notifications.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The boundary is a position rather than an offset, so a notification raised while somebody is paging neither
    /// shifts the window nor causes a row to be repeated or skipped. Whether the cursor was issued to this owner is
    /// the caller's question rather than the store's: this reads the owner it is given and nothing else.
    /// </remarks>
    Task<IReadOnlyList<Notification>> ReadPageAsync(
        MailOwnerId owner,
        NotificationCursor? after,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Counts one owner's notifications that have not been read.</summary>
    /// <param name="owner">The owner whose notifications are counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many of the owner's notifications stand unread.</returns>
    /// <remarks>
    /// It is a count rather than the length of a page, because the one thing a client asks for most often is the badge
    /// beside the bell and reading a page to derive it would serve a screen's worth of rows to produce one number.
    /// </remarks>
    Task<int> CountUnreadAsync(MailOwnerId owner, CancellationToken cancellationToken);

    /// <summary>Puts one of an owner's notifications into a stated read state.</summary>
    /// <param name="owner">The owner whose notification is changed, which is what scopes the write.</param>
    /// <param name="notification">The notification to change.</param>
    /// <param name="isRead">The read state it is to stand in.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the request.</returns>
    /// <remarks>
    /// The owner is part of the addressing rather than a filter applied after a lookup, so a notification another
    /// owner holds is not found rather than found and refused — which is what keeps the answer identical for one that
    /// does not exist at all.
    /// </remarks>
    Task<NotificationReadOutcome> SetReadAsync(
        MailOwnerId owner,
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken);

    /// <summary>Marks every one of an owner's unread notifications read.</summary>
    /// <param name="owner">The owner whose notifications are marked.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many notifications the request changed, which is zero where none stood unread.</returns>
    /// <remarks>
    /// It is unbounded on purpose, unlike the retention pass beside it: what it writes is one owner's own unread rows,
    /// which the deduplication rule already holds to one per standing condition, and a bound would leave a person
    /// pressing the control repeatedly to reach an empty centre.
    /// </remarks>
    Task<int> MarkAllReadAsync(MailOwnerId owner, CancellationToken cancellationToken);

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

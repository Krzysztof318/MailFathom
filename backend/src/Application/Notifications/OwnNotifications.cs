// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Reads and marks the signed-in person's own notification centre.</summary>
/// <remarks>
/// <para>
/// Whose notifications these are comes from the principal rather than from the request, exactly as it does for the
/// owner record and the client preferences: there is no argument here for another owner's identifier, so a reading of
/// somebody else's centre is something a caller cannot express rather than something a surface has to refuse. A
/// notification named by identifier is addressed with the owner beside it, so one another person holds answers as one
/// that does not exist.
/// </para>
/// <para>
/// Every act is admitted under <see cref="MailFathomPermission.MailRead" />, which is the grant a signed-in person
/// already holds, and none of them adds a name to the published set. Marking a notification read is deliberately not a
/// write grant: it changes what this deployment draws for one person about mail they can already see, reaches no mail
/// server, and a person whose mail accounts an administrator maintains has to be able to clear their own bell.
/// </para>
/// <para>
/// A page is bounded and the bound is clamped rather than refused, which is the one place this reading differs from
/// every other paged one here. Those serve an operator composing a query by hand, where a page size out of range is a
/// mistake worth naming; this serves a panel that asks for as much as it can draw, where the useful answer to "give me
/// a thousand" is the most this deployment serves rather than an error a screen has to render instead of a list.
/// </para>
/// </remarks>
public sealed class OwnNotifications
{
    /// <summary>The page size a request that names none is served.</summary>
    /// <remarks>Enough to fill the panel a client draws without scrolling, so a first paint is one request.</remarks>
    public const int DefaultPageSize = 30;

    /// <summary>The greatest page size one request is served, whatever it asked for.</summary>
    /// <remarks>
    /// A notification is a title, a second line, and a pointer, so a page of this many is a few tens of kilobytes
    /// rather than the megabyte a page of mail would be. It is the bound rather than a refusal: see the type's own
    /// remarks for why this reading clamps where the others refuse.
    /// </remarks>
    public const int MaximumPageSize = 100;

    private readonly AccessAuthorization authorization;
    private readonly INotificationStore store;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the owner it acts for.</param>
    /// <param name="store">Holds what happened to a person.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnNotifications(AccessAuthorization authorization, INotificationStore store)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(store);

        this.authorization = authorization;
        this.store = store;
    }

    /// <summary>Reads one page of the signed-in person's notifications, newest first.</summary>
    /// <param name="pageSize">How many notifications the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />; a larger number is served <see cref="MaximumPageSize" />.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the newest page.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The page, or <see langword="null" /> when the cursor is not one this deployment issued to this caller.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// A page size below one is served the default rather than refused, for the reason a page size above the maximum is
    /// clamped: both are a client asking for something no screen wants, and the number this deployment serves is a
    /// better answer than a refusal a panel has to draw.
    /// </remarks>
    public async Task<NotificationPage?> ReadPageAsync(
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var owner = this.authorization.RequireOwner();
        var fingerprint = NotificationCursor.FingerprintOf(owner);
        NotificationCursor? boundary = null;

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!NotificationCursor.TryDecode(cursor, out boundary)
                || !string.Equals(boundary!.Value.OwnerFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return null;
            }
        }

        var limit = Bounded(pageSize);
        var notifications = await this.store.ReadPageAsync(owner, boundary, limit, cancellationToken);

        // The page is short only where the centre held nothing more, so the boundary is issued exactly when a full
        // page came back — which is what lets a caller stop on the absent cursor rather than on a length comparison.
        var next = notifications.Count == limit && notifications[^1] is { } last
            ? NotificationCursor.After(last.OccurredAt, last.Id, fingerprint)
            : (NotificationCursor?)null;

        return new NotificationPage(notifications, next);
    }

    /// <summary>Counts what the signed-in person has not read.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many of their notifications stand unread.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<int> CountUnreadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.CountUnreadAsync(this.authorization.RequireOwner(), cancellationToken);
    }

    /// <summary>Puts one of the signed-in person's notifications into a stated read state.</summary>
    /// <param name="notification">The notification to change.</param>
    /// <param name="isRead">The read state it is to stand in.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What became of the request.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<NotificationReadOutcome> SetReadAsync(
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.SetReadAsync(
            this.authorization.RequireOwner(),
            notification,
            isRead,
            cancellationToken);
    }

    /// <summary>Marks every one of the signed-in person's unread notifications read.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>How many notifications the request changed.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no owner, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    public Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return this.store.MarkAllReadAsync(this.authorization.RequireOwner(), cancellationToken);
    }

    /// <summary>Reduces what a caller asked for to a page size this deployment serves.</summary>
    private static int Bounded(int? pageSize) => pageSize switch
    {
        null or < 1 => DefaultPageSize,
        > MaximumPageSize => MaximumPageSize,
        var asked => asked.Value,
    };
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Notifications;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person their own notification centre: what happened, how much of it is unread, and both ways of marking it read.</summary>
/// <remarks>
/// <para>
/// Four routes over one person's own working state. The centre is a list newest first, a count on its own for the bell
/// that draws a badge without opening the panel, one notification's read state, and the control that clears the lot.
/// </para>
/// <para>
/// <b>Nothing here streams.</b> This surface has no server-sent events and gains none for this, so a client asks on an
/// interval and when it comes back to the foreground. The count is the answer it asks for most, which is why it stands
/// on its own route rather than being derived from a page: a badge that cost a screen's worth of rows to draw would be
/// the most expensive thing on a polling client's schedule.
/// </para>
/// <para>
/// <b>No route names an owner.</b> The person is the one the credential authenticated, resolved exactly as the record
/// and preferences routes resolve it, so a reading of somebody else's centre cannot be composed. The one route that
/// does name a record names it by identifier, and a notification another person holds answers <c>404</c> exactly as one
/// nobody holds — so nothing here reports whether such a notification exists.
/// </para>
/// <para>
/// <b>Every route is <see cref="MailFathomPermission.MailRead" />, the two writes included.</b> Marking a notification
/// read changes what this deployment draws for one person about mail they can already see; it reaches no mail server
/// and moves nothing in a mailbox. It is the preferences write's reasoning rather than the mutation routes': a person
/// whose mail accounts an administrator maintains does not hold a write grant and still has to be able to clear their
/// own bell. Nothing here is a power a credential granted to read a mailbox did not already have.
/// </para>
/// <para>
/// <b>The page is clamped rather than refused.</b> Every other paged reading in this repository serves an operator
/// composing a query by hand, where a page size out of range is a mistake worth naming. This one serves a panel asking
/// for as much as it can draw, so a request for more than <see cref="OwnNotifications.MaximumPageSize" /> is answered
/// with that many. A cursor is the one thing here that is refused, because a boundary this deployment never issued
/// names no page and answering it with the newest one would be a panel silently jumping back to the top.
/// </para>
/// </remarks>
internal static class ClientNotificationEndpoints
{
    /// <summary>The route one page of the acting person's notifications is read from, relative to the client prefix.</summary>
    internal const string NotificationsRoute = "/notifications";

    /// <summary>The route the acting person's unread count is read from, on its own.</summary>
    internal const string UnreadCountRoute = $"{NotificationsRoute}/unread-count";

    /// <summary>The route every one of the acting person's notifications is marked read on.</summary>
    internal const string MarkAllReadRoute = $"{NotificationsRoute}/read";

    /// <summary>The route one notification's read state is stated on.</summary>
    /// <remarks>
    /// One route carrying the state rather than a path per direction, which is where this differs from the outbox's
    /// two. Those are opposite decisions about a message that may already be gone, so a mistyped value there is the
    /// difference between withdrawing a message and sending it again; this is one reversible switch about a row on a
    /// screen, and a second path would be a second route for the undo of the first.
    /// </remarks>
    internal const string ReadStateRoute = $"{NotificationsRoute}/{{notificationId:guid}}/read-state";

    /// <summary>The greatest request body the read-state route reads before refusing it.</summary>
    /// <remarks>
    /// The body is one boolean, so the bound guards against a body that was never a read-state document at all rather
    /// than against a large one. It is answered <c>413</c> before the handler is reached, as every other write on this
    /// surface is.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 1024;

    /// <summary>Maps the notification routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientNotifications(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(NotificationsRoute, ReadPageAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapGet(UnreadCountRoute, ReadUnreadCountAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(ReadStateRoute, SetReadStateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPost(MarkAllReadRoute, MarkAllReadAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one page of the acting person's notifications, newest first.</summary>
    /// <param name="pageSize">How many notifications the page may hold, or <see langword="null" /> for the default; a larger number is served the maximum.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the newest page.</param>
    /// <param name="notifications">Reads the page, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> where the cursor is not one this deployment issued to this caller.</returns>
    internal static async Task<Results<Ok<ClientNotificationPageResponse>, ProblemHttpResult>> ReadPageAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OwnNotifications notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var page = await notifications.ReadPageAsync(pageSize, cursor, cancellationToken);

        return page is null
            ? Refuse("The cursor is not one this deployment issued for your notifications.")
            : TypedResults.Ok(ClientNotificationPageResponse.For(page));
    }

    /// <summary>Serves how many of the acting person's notifications stand unread.</summary>
    /// <param name="notifications">Counts the unread ones, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the count.</returns>
    internal static async Task<Ok<ClientUnreadNotificationCountResponse>> ReadUnreadCountAsync(
        [FromServices] OwnNotifications notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return TypedResults.Ok(
            new ClientUnreadNotificationCountResponse(await notifications.CountUnreadAsync(cancellationToken)));
    }

    /// <summary>Puts one of the acting person's notifications into the read state the body states.</summary>
    /// <param name="notificationId">The notification to change.</param>
    /// <param name="request">The state it is to stand in.</param>
    /// <param name="notifications">Performs the change, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with what a client redraws from, <c>404</c> where this person holds no such notification, or <c>409</c> where the condition already stands unread.</returns>
    /// <remarks>
    /// The answer carries the new state and the count beside it, so a client redraws the row and the badge from one
    /// response rather than fetching the page again to find out what its own request produced.
    /// </remarks>
    internal static async Task<Results<Ok<ClientNotificationReadStateResponse>, NotFound, ProblemHttpResult>> SetReadStateAsync(
        [FromRoute] Guid notificationId,
        [FromBody] ClientNotificationReadStateRequest request,
        [FromServices] OwnNotifications notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(request);

        if (notificationId == Guid.Empty)
        {
            return TypedResults.NotFound();
        }

        var outcome = await notifications.SetReadAsync(
            NotificationId.Create(notificationId),
            request.Read,
            cancellationToken);

        return outcome switch
        {
            NotificationReadOutcome.NotFound => TypedResults.NotFound(),
            NotificationReadOutcome.ConditionAlreadyStanding => TypedResults.Problem(
                "A newer notification about the same thing is already unread, so this one stays read.",
                statusCode: StatusCodes.Status409Conflict),
            _ => TypedResults.Ok(new ClientNotificationReadStateResponse(
                notificationId,
                request.Read,
                await notifications.CountUnreadAsync(cancellationToken))),
        };
    }

    /// <summary>Marks every one of the acting person's unread notifications read.</summary>
    /// <param name="notifications">Performs the change, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><c>200</c> with how many were marked and the count that leaves.</returns>
    /// <remarks>
    /// The count it answers with is zero by construction rather than by a second read: the request marked every unread
    /// notification this person holds, and one raised after it is a poll away rather than something this answer was
    /// ever going to carry.
    /// </remarks>
    internal static async Task<Ok<ClientMarkedNotificationsResponse>> MarkAllReadAsync(
        [FromServices] OwnNotifications notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        return TypedResults.Ok(
            new ClientMarkedNotificationsResponse(await notifications.MarkAllReadAsync(cancellationToken), 0));
    }

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The read state a person states for one of their notifications.</summary>
/// <param name="Read">Whether the notification is to stand read.</param>
/// <remarks>
/// Bound strictly: a key nothing here binds fails the bind rather than being ignored, so a client that meant to send
/// something else is told rather than having its request read as the opposite of what it wrote.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientNotificationReadStateRequest(bool Read);

/// <summary>One page of what happened to a person, newest first.</summary>
/// <param name="Notifications">The notifications, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the centre.</param>
internal sealed record ClientNotificationPageResponse(
    IReadOnlyList<ClientNotificationResponse> Notifications,
    string? NextCursor)
{
    /// <summary>Describes one page on the wire.</summary>
    /// <param name="page">The page that was read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static ClientNotificationPageResponse For(NotificationPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ClientNotificationPageResponse(
            [.. page.Notifications.Select(ClientNotificationResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One thing that happened to a person while nobody was looking at the screen.</summary>
/// <param name="Id">What addresses the notification, and what the read-state route names it by.</param>
/// <param name="Kind">What part of MailFathom it is about, which is what a row is drawn and grouped by.</param>
/// <param name="Title">The headline the row is drawn with.</param>
/// <param name="Body">The second line the row is drawn with.</param>
/// <param name="Source">What the source line names beyond the kind, or <see langword="null" /> where the kind is the whole of it.</param>
/// <param name="Target">Where opening it leads.</param>
/// <param name="OccurredAt">When the thing it describes happened.</param>
/// <param name="Read">Whether the person has read it.</param>
/// <remarks>
/// It carries what a row draws and stops there. The title and the second line were derived when the notification was
/// produced, so nothing here re-reads mail to draw a list — and no mail body, no address, and no attachment reaches
/// this answer at any size. The condition the notification was raised for is deliberately absent as well: it is the
/// deduplication rule's own name for a thing rather than anything a screen has to render.
/// </remarks>
internal sealed record ClientNotificationResponse(
    Guid Id,
    string Kind,
    string Title,
    string Body,
    string? Source,
    ClientNotificationTargetResponse Target,
    DateTimeOffset OccurredAt,
    bool Read)
{
    /// <summary>Describes one notification on the wire.</summary>
    /// <param name="notification">The notification.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification" /> is <see langword="null" />.</exception>
    internal static ClientNotificationResponse For(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return new ClientNotificationResponse(
            notification.Id.Value,
            notification.Kind.ToString(),
            notification.Title,
            notification.Body,
            notification.Source,
            ClientNotificationTargetResponse.For(notification.Target),
            notification.OccurredAt,
            notification.IsRead);
    }
}

/// <summary>Where opening a notification leads.</summary>
/// <param name="Kind">Which of the three shapes this target is.</param>
/// <param name="MessageId">The stored message it leads to, or <see langword="null" /> for every other shape.</param>
/// <param name="Screen">The screen it leads to, or <see langword="null" /> for every other shape.</param>
/// <remarks>
/// The shape is reported beside the two values so a client switches on one field rather than inferring the shape from
/// which value happens to be present — which is what keeps a target that leads nowhere distinct from one whose record
/// this build could not name.
/// </remarks>
internal sealed record ClientNotificationTargetResponse(string Kind, Guid? MessageId, string? Screen)
{
    /// <summary>Describes one target on the wire.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    internal static ClientNotificationTargetResponse For(NotificationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return new ClientNotificationTargetResponse(
            target.Kind.ToString(),
            target.Message?.Value,
            target.Screen?.ToString());
    }
}

/// <summary>How much of a person's centre stands unread.</summary>
/// <param name="UnreadCount">How many of their notifications have not been read.</param>
internal sealed record ClientUnreadNotificationCountResponse(int UnreadCount);

/// <summary>What one notification's read state now is, and what that leaves on the bell.</summary>
/// <param name="Id">The notification that was changed.</param>
/// <param name="Read">The state it now stands in.</param>
/// <param name="UnreadCount">How many of the person's notifications remain unread.</param>
internal sealed record ClientNotificationReadStateResponse(Guid Id, bool Read, int UnreadCount);

/// <summary>What marking the whole centre read changed.</summary>
/// <param name="MarkedRead">How many notifications the request moved, which is zero where none stood unread.</param>
/// <param name="UnreadCount">How many remain unread, which this request leaves at none.</param>
internal sealed record ClientMarkedNotificationsResponse(int MarkedRead, int UnreadCount);

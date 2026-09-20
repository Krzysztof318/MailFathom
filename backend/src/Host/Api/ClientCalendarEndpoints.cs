// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person their own calendar: the window every view over it is, and the five acts they perform on an event.</summary>
/// <remarks>
/// <para>
/// <b>Every read is a window, because every view is one.</b> A month, a week, a day, and an agenda are four spans
/// rather than four questions, so one route answers them all and a client states the span it drew. The half of the
/// calendar is stated beside it: the dates mail proposed are drawn somewhere other than the calendar itself, so a
/// caller asks for one half or the other rather than sorting the answer out afterwards.
/// </para>
/// <para>
/// <b>No route names a person.</b> The calendar reached is the one the credential authenticated, resolved exactly as
/// the record, preferences, and notification routes resolve it, so a reading of somebody else's days cannot be
/// composed. An event another person holds answers <c>404</c> exactly as one nobody holds, which is what keeps this
/// surface from reporting whose calendars exist beside the caller's own.
/// </para>
/// <para>
/// <b>Accepting a proposal is a route and dismissing one is not.</b> Acceptance changes what the record claims, so it
/// is published as the act it is; dismissal removes the row, which is the same operation as deleting an event a person
/// typed — a date nobody wanted is not a fact worth keeping, and a second path for it would be one route meaning
/// exactly what its neighbour means.
/// </para>
/// <para>
/// Every route is <see cref="MailFathomPermission.MailRead" />, the four writes included, exactly as the task routes
/// beside them are: what they serve is a record native to this deployment rather than a mailbox, and a person whose
/// mail accounts an administrator maintains still has to be able to keep their own calendar.
/// </para>
/// <para>
/// <b>Nothing an event carries reaches a refusal.</b> Every message a refusal states comes from this system rather
/// than from the request, so a title that was not accepted is reported as a title that is not usable instead of being
/// echoed into a problem document a proxy log keeps.
/// </para>
/// </remarks>
internal static class ClientCalendarEndpoints
{
    /// <summary>The route a window of the acting person's calendar is read from and an event is written to, relative to the client prefix.</summary>
    internal const string CalendarRoute = "/calendar";

    /// <summary>The route one event is read, amended, and deleted at, relative to the client prefix.</summary>
    internal const string CalendarEventRoute = $"{CalendarRoute}/{{eventId:guid}}";

    /// <summary>The route a proposed event is taken onto the calendar at, relative to the client prefix.</summary>
    /// <remarks>
    /// A segment of its own because acceptance is not an amendment: it states no record, changes what the event claims
    /// rather than what it says, and is refused on an event that is already on the calendar.
    /// </remarks>
    internal const string CalendarEventAcceptanceRoute = $"{CalendarEventRoute}/acceptance";

    /// <summary>The greatest request body a calendar write reads before refusing it.</summary>
    /// <remarks>
    /// A full request is one title, two instants, an identifier, and a short list of whole numbers, which is under a
    /// kilobyte however it is escaped. This stands well above that, so a body that was never an event at all is
    /// answered <c>413</c> before the handler is reached rather than parsed.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 4 * 1024;

    /// <summary>Maps the calendar routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientCalendar(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(CalendarRoute, ReadWindowAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapGet(CalendarEventRoute, FindAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, exactly as every other write on this
        // surface reaches it: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the
        // request body feature, so a body over the bound is answered 413 before the handler is reached.
        api.MapPost(CalendarRoute, CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPut(CalendarEventRoute, AmendAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPost(CalendarEventAcceptanceRoute, AcceptAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapDelete(CalendarEventRoute, DeleteAsync)
            .RequirePermission(MailFathomPermission.MailRead);
    }

    /// <summary>Serves one window of the acting person's calendar, earliest first.</summary>
    /// <param name="from">The instant the window opens, which an event ending exactly there falls outside.</param>
    /// <param name="until">The instant it closes, which an event beginning exactly there falls outside.</param>
    /// <param name="origin">The half to read — <c>Asserted</c> or <c>Proposed</c> — or nothing for both.</param>
    /// <param name="count">How many events the window may answer with, or nothing for the default.</param>
    /// <param name="calendar">Reads the calendar of the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the window, or <c>400</c> naming what was wrong with what it asked for.</returns>
    /// <remarks>
    /// There is no unbounded reading of a calendar, and a window outside what this deployment answers is refused rather
    /// than narrowed: a screen that asked for a year and was served a month would be missing eleven of them while
    /// believing it drew the year.
    /// </remarks>
    internal static async Task<Results<Ok<CalendarWindowResponse>, ProblemHttpResult>> ReadWindowAsync(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? until,
        [FromQuery] string? origin,
        [FromQuery] int? count,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (from is not { } opens || until is not { } closes)
        {
            return Refuse("A calendar window states the instant it opens and the instant it closes.");
        }

        if (!TryReadOrigin(origin, out var narrowed))
        {
            return Refuse("A calendar window reads the asserted half or the proposed half, or names neither for both.");
        }

        var window = await calendar.ReadWindowAsync(opens, closes, narrowed, count, cancellationToken);

        return window is null
            ? Refuse(
                "A calendar window closes after it opens and answers with between 1 and "
                + $"{CalendarEventQuery.MaximumCount} events.")
            : TypedResults.Ok(new CalendarWindowResponse([.. window.Select(CalendarEventResponse.For)]));
    }

    /// <summary>Serves one event of the acting person's calendar.</summary>
    /// <param name="eventId">The event to read.</param>
    /// <param name="calendar">Reads the calendar of the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the event, or <c>404</c> where that calendar holds no such event.</returns>
    internal static async Task<Results<Ok<CalendarEventResponse>, NotFound>> FindAsync(
        [FromRoute] Guid eventId,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (NamedEvent(eventId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await calendar.FindAsync(identity, cancellationToken) is { } held
            ? TypedResults.Ok(CalendarEventResponse.For(held))
            : TypedResults.NotFound();
    }

    /// <summary>Puts an event the acting person states on their own calendar.</summary>
    /// <param name="request">The event to write.</param>
    /// <param name="calendar">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the event as it now stands, or <c>400</c> naming which rule it broke.</returns>
    /// <remarks>What is created here is on the calendar rather than offered to it: nothing a person types is a proposal.</remarks>
    internal static async Task<Results<Ok<CalendarEventResponse>, ProblemHttpResult>> CreateAsync(
        [FromBody] CalendarEventCreationRequest? request,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (request is null)
        {
            return NoEvent();
        }

        if (request.Start is not { } start)
        {
            return Refuse("An event states when it begins.");
        }

        var (cited, namesAMessage) = SourceMessageOf(request.SourceMessage);

        if (!namesAMessage)
        {
            return Refuse("A message identifier cannot be empty.");
        }

        var written = await calendar.CreateAsync(
            request.Title,
            start,
            request.End,
            request.IsAllDay,
            request.Reminders ?? [],
            cited,
            cancellationToken);

        return written.Outcome is CalendarEventWriteOutcome.Written
            ? TypedResults.Ok(CalendarEventResponse.For(written.Event!))
            : Refuse(Stated(written.Outcome));
    }

    /// <summary>Amends one event of the acting person's calendar to the record they state.</summary>
    /// <param name="eventId">The event to amend.</param>
    /// <param name="request">The record the event is to have afterwards.</param>
    /// <param name="calendar">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the amended event, <c>404</c> where that calendar holds no such event, or <c>400</c> naming which rule the record broke.</returns>
    internal static async Task<Results<Ok<CalendarEventResponse>, NotFound, ProblemHttpResult>> AmendAsync(
        [FromRoute] Guid eventId,
        [FromBody] CalendarEventAmendmentRequest? request,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (NamedEvent(eventId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        if (request is null)
        {
            return NoEvent();
        }

        if (request.Start is not { } start)
        {
            return Refuse("An event states when it begins.");
        }

        var written = await calendar.AmendAsync(
            identity,
            request.Title,
            start,
            request.End,
            request.IsAllDay,
            request.Reminders ?? [],
            cancellationToken);

        return Answer(written);
    }

    /// <summary>Takes a date the acting person's mail proposed onto their calendar.</summary>
    /// <param name="eventId">The proposal to accept.</param>
    /// <param name="calendar">Performs the write.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the event as it now stands, <c>404</c> where that calendar holds no such event, or <c>409</c> where it is already on the calendar.</returns>
    /// <remarks>
    /// The one write here that states no record, which is why it answers with the calendar's own contents rather than
    /// with the request's. Accepting an event that is already on the calendar is a conflict rather than a repeat: the
    /// act the caller is reporting was performed by somebody else, and answering it as done would move the record of
    /// when the event actually reached the calendar.
    /// </remarks>
    internal static async Task<Results<Ok<CalendarEventResponse>, NotFound, ProblemHttpResult>> AcceptAsync(
        [FromRoute] Guid eventId,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (NamedEvent(eventId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return Answer(await calendar.AcceptAsync(identity, cancellationToken));
    }

    /// <summary>Takes one event off the acting person's calendar, whether they put it there or their mail proposed it.</summary>
    /// <param name="eventId">The event to delete.</param>
    /// <param name="calendar">Performs the deletion.</param>
    /// <param name="cancellationToken">Cancels the deletion when the client disconnects.</param>
    /// <returns><c>204</c> where the event is gone, or <c>404</c> where that calendar holds no such event.</returns>
    /// <remarks>
    /// This is also how a proposal is dismissed. The row goes rather than being marked, so nothing restores one and a
    /// calendar that appears to hold nothing at a date holds nothing at it.
    /// </remarks>
    internal static async Task<Results<NoContent, NotFound>> DeleteAsync(
        [FromRoute] Guid eventId,
        [FromServices] OwnCalendar calendar,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        if (NamedEvent(eventId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await calendar.DeleteAsync(identity, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    /// <summary>Answers a write that could have named an event the calendar does not hold.</summary>
    private static Results<Ok<CalendarEventResponse>, NotFound, ProblemHttpResult> Answer(
        CalendarEventWriteResult written)
    {
        return written.Outcome switch
        {
            CalendarEventWriteOutcome.Written => TypedResults.Ok(CalendarEventResponse.For(written.Event!)),
            CalendarEventWriteOutcome.NotFound => TypedResults.NotFound(),
            CalendarEventWriteOutcome.AlreadyOnTheCalendar => TypedResults.Problem(
                Stated(written.Outcome),
                statusCode: StatusCodes.Status409Conflict),
            _ => Refuse(Stated(written.Outcome)),
        };
    }

    /// <summary>States what a caller has to change, without echoing anything the event carries.</summary>
    private static string Stated(CalendarEventWriteOutcome outcome) => outcome switch
    {
        CalendarEventWriteOutcome.TitleRefused =>
            $"An event title is non-blank, at most {CalendarEventTitle.MaximumLength} characters, and carries no "
            + "control or format character.",
        CalendarEventWriteOutcome.EndNotAfterStart => "An event that states an end ends after it begins.",
        CalendarEventWriteOutcome.AlreadyOnTheCalendar =>
            "The event is already on the calendar, so there is no proposal left to accept.",
        CalendarEventWriteOutcome.RemindersRefused =>
            $"An event carries at most {CalendarEvent.MaximumReminderCount} reminders, each stated once, as whole "
            + $"minutes between 0 and {CalendarReminder.MaximumMinutesBefore} before it.",
        _ => "The event cannot be written as stated.",
    };

    /// <summary>Reads the half of the calendar a window names, admitting the published spellings and nothing else.</summary>
    /// <remarks>
    /// The names rather than <c>Enum.TryParse</c>, which also reads the numbers behind them: a client composing
    /// <c>origin=1</c> would be relying on an ordering this repository is free to change, and the spelling it reads
    /// back off every event is the name.
    /// </remarks>
    private static bool TryReadOrigin(string? stated, out CalendarEventOrigin? origin)
    {
        origin = null;

        if (string.IsNullOrWhiteSpace(stated))
        {
            return true;
        }

        foreach (var published in Enum.GetValues<CalendarEventOrigin>())
        {
            if (string.Equals(stated, published.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                origin = published;

                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the event a route named, refusing the one value a UUID route constraint still admits.</summary>
    /// <remarks>
    /// An event identifier is never empty, and the constraint on the route cannot say so: it accepts the all-zero UUID
    /// like any other. Keeping it out here is what stops a caller that composed one meeting a stated answer rather than
    /// an unhandled guard reported as a fault in the deployment.
    /// </remarks>
    private static CalendarEventId? NamedEvent(Guid eventId) =>
        eventId == Guid.Empty ? null : CalendarEventId.Create(eventId);

    /// <summary>Reads the message an event cites, refusing the one value that names no message.</summary>
    private static (StoredEmailId? Cited, bool Stated) SourceMessageOf(Guid? sourceMessage) => sourceMessage switch
    {
        null => (null, true),
        { } named when named == Guid.Empty => (null, false),
        { } named => (StoredEmailId.Create(named), true),
    };

    /// <summary>States that the request carried no event to write.</summary>
    private static ProblemHttpResult NoEvent() => Refuse("The request carries no calendar event.");

    /// <summary>States what a caller has to change, without echoing anything about the event it was writing.</summary>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

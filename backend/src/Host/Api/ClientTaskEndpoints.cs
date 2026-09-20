// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using MailFathom.Host.Security.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MailFathom.Host.Api;

/// <summary>Serves the signed-in person their own task list: the two halves they read it as, and the six acts they perform on one task.</summary>
/// <remarks>
/// <para>
/// A list native to this deployment rather than a view onto anything else: no external task-management protocol is
/// spoken here, and what a person sees is what this database holds for them. The store beneath it is the one every
/// producer writes through, so a task a screen created and a task mail proposed are one kind of row.
/// </para>
/// <para>
/// <b>The list is read as two, because it is two.</b> What somebody has committed to and what mail suggested and
/// nobody has agreed to yet are different things on a screen — one is a list of what is owed, the other an offer — so
/// each is its own paginated route rather than one listing with a filter a client has to know to send. Both walk the
/// same order, and a cursor is refused in the half it was not issued for, which is what stops a walk of the proposals
/// continuing into the commitments halfway down a screen.
/// </para>
/// <para>
/// <b>No route names a user.</b> The person is the one the credential authenticated, resolved exactly as the record,
/// preferences, and notification routes resolve it, so a reading of somebody else's list cannot be composed. Every
/// route that names a task names it by identifier, and a task another person holds answers <c>404</c> exactly as one
/// nobody holds — so nothing here reports whether such a task exists.
/// </para>
/// <para>
/// <b>Dismissing a proposal is the erasure rather than a route of its own.</b> Declining what mail suggested and
/// deleting something a person owes leave the same list behind, so publishing both would be two names for one act,
/// told apart only by the word a screen puts on the button.
/// </para>
/// <para>
/// <b>Every route that reads or writes the list is <see cref="MailFathomPermission.MailRead" />, the five writes
/// included.</b> A task is this deployment's own record of what one person owes: nothing here reaches a mail server,
/// nothing moves in a mailbox, and the citation a task carries is a value rather than a reading of mail. It is the
/// notification centre's reasoning rather than the mutation routes' — a person whose mail accounts an administrator
/// maintains does not hold a write grant and still has to be able to keep their own list.
/// </para>
/// <para>
/// <b>The day-layout pair is <see cref="MailFathomPermission.MailAsk" /> instead, and the arrangement asks
/// <see cref="MailFathomPermission.MailRead" /> beneath it.</b> What the published grant follows is where the lines
/// go: arranging a day leaves this deployment for a chat provider and is charged to the same allowance a question is.
/// What the arrangement then reads to compose that turn is this list and the day's calendar, both of them the reading
/// grant's own records, so the use case beneath asks for it on its own and a caller holding the asking grant alone is
/// refused naming the one they are missing — the shape the contact card already stands in. The route that only says
/// whether a day is arranged at all reads neither, and answers on the asking grant alone.
/// </para>
/// </remarks>
internal static class ClientTaskEndpoints
{
    /// <summary>The route the acting person's committed tasks are listed and written to, relative to the client prefix.</summary>
    internal const string TasksRoute = "/tasks";

    /// <summary>The route the tasks mail proposed and nobody has accepted are listed at, relative to the client prefix.</summary>
    /// <remarks>
    /// A literal segment where the single-task route takes an identifier, which routing prefers over a parameter, so
    /// the two cannot be confused. The segment names the origin rather than an action, because what it lists is the
    /// half of the list the person has not agreed to.
    /// </remarks>
    internal const string ProposedTasksRoute = $"{TasksRoute}/proposed";

    /// <summary>The route one task is read, revised, and erased at, relative to the client prefix.</summary>
    internal const string TaskRoute = $"{TasksRoute}/{{taskId:guid}}";

    /// <summary>The route one task's completion state is stated on.</summary>
    /// <remarks>
    /// One route carrying the state rather than a path per direction, for the reason the notification read state is
    /// one: completing something and finding out it was not done after all is one reversible switch, and a second path
    /// would be a second route for the undo of the first.
    /// </remarks>
    internal const string TaskCompletionRoute = $"{TaskRoute}/completion";

    /// <summary>The route a proposed task is taken on at.</summary>
    /// <remarks>
    /// A path of its own rather than a state on the route above, because it is not reversible in the same sense: what
    /// it moves is where the task came from, and a person who decides they do not owe it after all erases it rather
    /// than proposing it back to themselves.
    /// </remarks>
    internal const string TaskAcceptanceRoute = $"{TaskRoute}/acceptance";

    /// <summary>The route the acting person's day is arranged at, relative to the client prefix.</summary>
    /// <remarks>
    /// Two literal segments where the single-task route takes an identifier, so routing prefers this and the two
    /// cannot be confused. It is under the task list rather than beside it because what it arranges is that list —
    /// the calendar is read to say where the day is already spoken for, and nothing about it is answered here.
    /// </remarks>
    internal const string TodayLayoutRoute = $"{TasksRoute}/today/layout";

    /// <summary>The greatest request body a task write reads before refusing it.</summary>
    /// <remarks>
    /// A full request is a title bounded at <see cref="PersonalTask.MaximumTitleLength" /> characters, a day, an
    /// identifier, and a short list of whole numbers, which stands well inside this even where every character of the
    /// title is several bytes. A body over it was never a task document, and it is answered <c>413</c> before the
    /// handler is reached, as every other write on this surface is.
    /// </remarks>
    internal const int MaxWriteRequestBytes = 2 * 1024;

    /// <summary>The one form a day travels in on this surface.</summary>
    private const string DayFormat = "yyyy-MM-dd";

    /// <summary>Maps the task routes into the client group, so they inherit its requirement, its policy, and its limits.</summary>
    /// <param name="api">The client route group.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="api" /> is <see langword="null" />.</exception>
    internal static void MapClientTasks(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        api.MapGet(TasksRoute, ReadCommittedAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapGet(ProposedTasksRoute, ReadProposedAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapGet(TaskRoute, FindAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The attribute is reached for its metadata rather than as an MVC filter, for the reason the record routes
        // state: it implements IRequestSizeLimitMetadata, which the routing pipeline applies to the request body.
        api.MapPost(TasksRoute, RecordAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPut(TaskRoute, ReviseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPost(TaskCompletionRoute, SetCompletionAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapPost(TaskAcceptanceRoute, AcceptAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        api.MapDelete(TaskRoute, EraseAsync)
            .RequirePermission(MailFathomPermission.MailRead);

        // The two routes of the arrangement, under the grant that governs asking rather than the one the list is read
        // under: laying out a day is a provider call charged to the same allowance a question is, which is what
        // decides the grant here rather than which records are read to compose it. The use case beneath the
        // arrangement asks for the reading grant as well, since the list and the calendar it reads are that grant's
        // own records, so a caller holding this one alone is refused naming the one it is missing.
        api.MapGet(TodayLayoutRoute, ArrangesDays)
            .RequirePermission(MailFathomPermission.MailAsk);

        api.MapPost(TodayLayoutRoute, LayOutTodayAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxWriteRequestBytes))
            .RequirePermission(MailFathomPermission.MailAsk);
    }

    /// <summary>Says whether this deployment arranges a day at all.</summary>
    /// <param name="planner">Arranges a day, in whichever state the deployment left it.</param>
    /// <returns><c>200</c> saying whether days are arranged, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// A capability rather than a probe: it resolves a registration and calls nothing, so a screen asking it once
    /// costs no provider call and reads nobody's list. It exists for the reason the drafting capability does — a
    /// control that promises an arrangement over a deployment that cannot compose one fails a person at the one
    /// moment they trusted it.
    /// </remarks>
    internal static Ok<ClientDayLayoutAvailabilityResponse> ArrangesDays(
        [FromServices] IDayLayoutPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(planner);

        return TypedResults.Ok(new ClientDayLayoutAvailabilityResponse(planner.IsActive));
    }

    /// <summary>Suggests how the acting person's day could be arranged, and changes nothing.</summary>
    /// <param name="request">The window the person's day runs in, as their own client drew it.</param>
    /// <param name="layout">Composes the arrangement, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the arrangement when the client disconnects.</param>
    /// <returns><c>200</c> with the arrangement, <c>400</c> where the window is not a day this deployment arranges, <c>429</c> where the deployment has spent what it allows a provider, or <c>403</c> for a caller whose grant does not carry <c>mailfathom.mail.ask</c>.</returns>
    /// <remarks>
    /// <para>
    /// Nothing is written by asking. No task is rescheduled, no proposal is accepted, and no event is put on a
    /// calendar: what comes back is drawn beside the list the person already holds, and every one of those acts stays
    /// behind the route that owns it.
    /// </para>
    /// <para>
    /// A deployment that arranges no day answers <c>200</c> saying so rather than a failure, and so does a provider
    /// that did not answer. The two are one answer here for the reason the drafting route gives: what a client does
    /// about either is identical, and naming which would publish a deployment's configuration to every signed-in
    /// browser. The spent allowance is the one that travels, because falling back there would leave somebody pressing
    /// a control the last of the allowance has already been paid for, told nothing.
    /// </para>
    /// </remarks>
    internal static async Task<Results<Ok<ClientDayLayoutResponse>, ProblemHttpResult>> LayOutTodayAsync(
        [FromBody] ClientDayLayoutRequest? request,
        [FromServices] TodayLayout layout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (request is not { From: { } opens, Until: { } closes })
        {
            return Refuse("A day is arranged between a stated opening and closing instant.");
        }

        var derivation = await layout.SuggestAsync(opens, closes, cancellationToken);

        if (derivation is null)
        {
            return Refuse(
                $"A day closes after it opens and runs for at most {TodayLayout.MaximumDaySpan.TotalHours} hours.");
        }

        if (derivation.Withheld is DayLayoutWithholding.AllowanceExhausted)
        {
            return TypedResults.Problem(
                "This deployment has spent what it allows a provider for now, so the day was not arranged.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        return TypedResults.Ok(ClientDayLayoutResponse.For(derivation.Suggestion));
    }

    /// <summary>Serves one page of what the acting person has committed to, soonest due first.</summary>
    /// <param name="pageSize">How many tasks the page may hold, or <see langword="null" /> for the default; a larger number is served the maximum.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="tasks">Reads the page, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> where the cursor is not one this deployment issued for this reading.</returns>
    internal static Task<Results<Ok<ClientTaskPageResponse>, ProblemHttpResult>> ReadCommittedAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken) =>
        ReadPageAsync(PersonalTaskOrigin.Asserted, pageSize, cursor, tasks, cancellationToken);

    /// <summary>Serves one page of what mail proposed and the acting person has not accepted, soonest due first.</summary>
    /// <param name="pageSize">How many tasks the page may hold, or <see langword="null" /> for the default; a larger number is served the maximum.</param>
    /// <param name="cursor">The cursor a previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="tasks">Reads the page, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the page, or <c>400</c> where the cursor is not one this deployment issued for this reading.</returns>
    internal static Task<Results<Ok<ClientTaskPageResponse>, ProblemHttpResult>> ReadProposedAsync(
        [FromQuery] int? pageSize,
        [FromQuery] string? cursor,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken) =>
        ReadPageAsync(PersonalTaskOrigin.Proposed, pageSize, cursor, tasks, cancellationToken);

    /// <summary>Serves one of the acting person's tasks.</summary>
    /// <param name="taskId">The task to read.</param>
    /// <param name="tasks">Answers what the acting person's list holds.</param>
    /// <param name="cancellationToken">Cancels the read when the client disconnects.</param>
    /// <returns><c>200</c> with the task, or <c>404</c> where this person holds no such task.</returns>
    internal static async Task<Results<Ok<ClientTaskResponse>, NotFound>> FindAsync(
        [FromRoute] Guid taskId,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (NamedTask(taskId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await tasks.FindAsync(identity, cancellationToken) is { } held
            ? TypedResults.Ok(ClientTaskResponse.For(held))
            : TypedResults.NotFound();
    }

    /// <summary>Records a task the acting person has just committed to.</summary>
    /// <param name="request">The task to write.</param>
    /// <param name="tasks">Performs the write, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the write when the client disconnects.</param>
    /// <returns><c>200</c> with the task as it was written, or <c>400</c> naming what the request has to change.</returns>
    /// <remarks>
    /// What a person types into their own client is something they owe, so what this writes is asserted. A proposal
    /// arrives through the extraction that read the mail rather than through here, which is why no request may state
    /// an origin.
    /// </remarks>
    internal static async Task<Results<Ok<ClientTaskResponse>, ProblemHttpResult>> RecordAsync(
        [FromBody] ClientTaskRecordRequest? request,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (request is null)
        {
            return NoRecord();
        }

        if (Stated(request.Title) is not { } title)
        {
            return NoTitle();
        }

        if (!TryReadDueDay(request.DueOn, out var dueOn))
        {
            return NoDay();
        }

        if (!TryReadAnnouncement(request, dueOn, out var announcement))
        {
            return NoReminders();
        }

        // The all-zero identifier addresses no message and the domain refuses to wrap one, so it is read as a task
        // citing nothing rather than becoming a failed request: a caller that sent it named no message.
        StoredEmailId? cited = request.SourceMessageId is { } message && message != Guid.Empty
            ? StoredEmailId.Create(message)
            : null;

        var written = await tasks.RecordAsync(title, dueOn, announcement, cited, cancellationToken);

        return TypedResults.Ok(ClientTaskResponse.For(written));
    }

    /// <summary>Writes what the acting person edited about one of their tasks.</summary>
    /// <param name="taskId">The task to revise.</param>
    /// <param name="request">The line and the day it is to carry afterwards.</param>
    /// <param name="tasks">Performs the write, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with the task as it now stands, <c>404</c> where this person holds no such task, or <c>400</c> naming what the request has to change.</returns>
    /// <remarks>
    /// The whole of what an edit may state rather than the difference from what is held, which is what keeps renaming
    /// a task and taking the date off it one request. What it may not state is where the task came from, whether it is
    /// done, or which message it cites: each of those moves through the act that owns it, so a rename cannot turn a
    /// proposal into a commitment.
    /// </remarks>
    internal static async Task<Results<Ok<ClientTaskResponse>, NotFound, ProblemHttpResult>> ReviseAsync(
        [FromRoute] Guid taskId,
        [FromBody] ClientTaskRecordRequest? request,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (NamedTask(taskId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        if (request is null)
        {
            return NoRecord();
        }

        if (Stated(request.Title) is not { } title)
        {
            return NoTitle();
        }

        if (!TryReadDueDay(request.DueOn, out var dueOn))
        {
            return NoDay();
        }

        if (!TryReadAnnouncement(request, dueOn, out var announcement))
        {
            return NoReminders();
        }

        return await tasks.ReviseAsync(identity, title, dueOn, announcement, cancellationToken) is { } revised
            ? TypedResults.Ok(ClientTaskResponse.For(revised))
            : TypedResults.NotFound();
    }

    /// <summary>Puts one of the acting person's tasks into the completion state the body states.</summary>
    /// <param name="taskId">The task to change.</param>
    /// <param name="request">The state it is to stand in.</param>
    /// <param name="tasks">Performs the change, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with the task as it now stands, or <c>404</c> where this person holds no such task.</returns>
    /// <remarks>
    /// Asking for the state a task already stands in is answered as done rather than as an error, because a repeated
    /// press is the act the person wanted rather than a mistake to report.
    /// </remarks>
    internal static async Task<Results<Ok<ClientTaskResponse>, NotFound>> SetCompletionAsync(
        [FromRoute] Guid taskId,
        [FromBody] ClientTaskCompletionRequest request,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(request);

        if (NamedTask(taskId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await tasks.SetCompletionAsync(identity, request.Completed, cancellationToken) is { } changed
            ? TypedResults.Ok(ClientTaskResponse.For(changed))
            : TypedResults.NotFound();
    }

    /// <summary>Takes on one of the tasks mail proposed, so it becomes one the acting person owes.</summary>
    /// <param name="taskId">The task to accept.</param>
    /// <param name="tasks">Performs the write, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns><c>200</c> with the task as it now stands, or <c>404</c> where this person holds no such task.</returns>
    /// <remarks>
    /// It moves where the task came from on the row that already exists, so a commitment keeps the identity it was
    /// proposed under and whatever already points at it still points at it. Accepting one already accepted is answered
    /// as done.
    /// </remarks>
    internal static async Task<Results<Ok<ClientTaskResponse>, NotFound>> AcceptAsync(
        [FromRoute] Guid taskId,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        if (NamedTask(taskId) is not { } identity)
        {
            return TypedResults.NotFound();
        }

        return await tasks.AcceptAsync(identity, cancellationToken) is { } accepted
            ? TypedResults.Ok(ClientTaskResponse.For(accepted))
            : TypedResults.NotFound();
    }

    /// <summary>Erases one of the acting person's tasks, which is also how a proposal is dismissed.</summary>
    /// <param name="taskId">The task to erase.</param>
    /// <param name="tasks">Performs the erasure, for the person the credential names.</param>
    /// <param name="cancellationToken">Cancels the erasure when the client disconnects.</param>
    /// <returns><c>200</c> with what happened, including for a list that held no such task.</returns>
    /// <remarks>
    /// Erasing something already erased is the act the caller wanted rather than an error, so the answer says what
    /// happened and never what was asked for — which is also what keeps a task another person holds from being told
    /// apart from one that does not exist.
    /// </remarks>
    internal static async Task<Ok<ClientTaskErasureResponse>> EraseAsync(
        [FromRoute] Guid taskId,
        [FromServices] OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var erased = NamedTask(taskId) is { } identity
            && await tasks.EraseAsync(identity, cancellationToken);

        return TypedResults.Ok(new ClientTaskErasureResponse(taskId, erased));
    }

    /// <summary>Serves one bounded page of one half of the list.</summary>
    /// <remarks>
    /// A blank cursor is read as no cursor, because a client that sent an empty argument asked for the first page
    /// rather than presented a boundary this deployment did not issue.
    /// </remarks>
    private static async Task<Results<Ok<ClientTaskPageResponse>, ProblemHttpResult>> ReadPageAsync(
        PersonalTaskOrigin origin,
        int? pageSize,
        string? cursor,
        OwnTasks tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var page = await tasks.ReadPageAsync(
            origin,
            pageSize,
            string.IsNullOrWhiteSpace(cursor) ? null : cursor,
            cancellationToken);

        return page is null
            ? Refuse("The cursor is not one this deployment issued for this list.")
            : TypedResults.Ok(ClientTaskPageResponse.For(page));
    }

    /// <summary>Reads the task a route named, refusing the one value a UUID route constraint still admits.</summary>
    /// <remarks>
    /// A task identifier is never empty, and the constraint on the route cannot say so: it accepts the all-zero UUID
    /// like any other. Keeping it out here is what stops a caller that composed one meeting an unhandled guard
    /// reported as a fault in the deployment.
    /// </remarks>
    private static PersonalTaskId? NamedTask(Guid taskId) =>
        taskId == Guid.Empty ? null : PersonalTaskId.Create(taskId);

    /// <summary>Reads the title a request states, as the text the caller wrote rather than as anything judged.</summary>
    /// <remarks>
    /// The length is bounded here as well as in the domain, because this is the trust boundary: the domain refuses an
    /// overlong title by throwing, which is the right answer to a producer inside this process and the wrong one to a
    /// client, for whom it is a stated refusal naming the bound.
    /// </remarks>
    private static string? Stated(string? title) =>
        string.IsNullOrWhiteSpace(title) || title.Trim().Length > PersonalTask.MaximumTitleLength ? null : title;

    /// <summary>Reads the calendar day a request states a task is due on.</summary>
    /// <remarks>
    /// One format and the invariant culture, for the reason the phrasing route states: the value is composed by a
    /// program rather than typed by a person, and accepting whatever the server's culture would also parse is how a day
    /// and a month come to be read the wrong way round. A request naming no day is a task nobody has dated, which is a
    /// state rather than a malformed value.
    /// </remarks>
    private static bool TryReadDueDay(string? written, out DateOnly? dueOn)
    {
        dueOn = null;

        if (string.IsNullOrWhiteSpace(written))
        {
            return true;
        }

        if (!DateOnly.TryParseExact(written, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return false;
        }

        dueOn = day;

        return true;
    }

    /// <summary>Reads what a request states announces the task, refusing what no task may carry.</summary>
    /// <remarks>
    /// Asked here rather than caught out of the domain, for the reason the title and the day are: a lead somebody
    /// typed is something this surface reports on, and the record raising for it would be a fault in the deployment
    /// rather than an answer to them. A request naming no lead announces nothing, whatever offset it states, so a
    /// client that always sends its own offset is not thereby asking to be reminded.
    /// </remarks>
    private static bool TryReadAnnouncement(
        ClientTaskRecordRequest request,
        DateOnly? dueOn,
        out TaskAnnouncement announcement)
    {
        announcement = TaskAnnouncement.Silent;

        var stated = request.Reminders ?? [];

        if (stated.Count == 0)
        {
            return true;
        }

        if (dueOn is null
            || request.DueDayOffsetMinutes is not { } offsetMinutes
            || !IsStatableOffset(offsetMinutes)
            || stated.Count > Reminder.MaximumCount
            || stated.Any(minutesBefore => !Reminder.IsStatable(minutesBefore))
            || stated.Distinct().Count() != stated.Count)
        {
            return false;
        }

        announcement = new TaskAnnouncement(
            TimeSpan.FromMinutes(offsetMinutes),
            [.. stated.Select(Reminder.Create)]);

        return true;
    }

    /// <summary>Reports whether a stated offset is one a UTC offset can be.</summary>
    /// <remarks>
    /// Fourteen hours either way is the range <see cref="DateTimeOffset" /> itself admits, and a whole number of
    /// minutes is what every real zone states. Bounded here because the value reaches a constructor that throws
    /// rather than reports, and this is the trust boundary.
    /// </remarks>
    private static bool IsStatableOffset(int offsetMinutes) => offsetMinutes is >= -14 * 60 and <= 14 * 60;

    /// <summary>States that the request carried no task to write.</summary>
    private static ProblemHttpResult NoRecord() => Refuse("The request carries no task.");

    /// <summary>States that the request named no usable title.</summary>
    private static ProblemHttpResult NoTitle() =>
        Refuse($"A task is stated with a title of at most {PersonalTask.MaximumTitleLength} characters.");

    /// <summary>States that the request named a day this surface does not read.</summary>
    private static ProblemHttpResult NoDay() => Refuse($"A due day is written as {DayFormat}.");

    /// <summary>States what a set of reminders has to be for the task to carry it.</summary>
    private static ProblemHttpResult NoReminders() => Refuse(
        $"A task carries at most {Reminder.MaximumCount} reminders, each stated once, as whole minutes between 0 and "
        + $"{Reminder.MaximumMinutesBefore} before nine in the morning on its due day — and only where the task "
        + "states a due day and the whole-minute UTC offset that day runs in.");

    /// <summary>States what a caller has to change, without echoing what they sent.</summary>
    /// <remarks>
    /// Without echoing it because a task's title is a line a person wrote about their own correspondence, and a problem
    /// document is the one part of an answer a proxy log keeps.
    /// </remarks>
    private static ProblemHttpResult Refuse(string stated) =>
        TypedResults.Problem(stated, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>The line and the day a person states for one task of their own.</summary>
/// <param name="Title">The line the list is drawn with, bounded at <see cref="PersonalTask.MaximumTitleLength" /> characters.</param>
/// <param name="DueOn">The day it is due on as <c>yyyy-mm-dd</c>, or <see langword="null" /> where nobody has said when.</param>
/// <param name="Reminders">The leads to announce it at, in minutes before nine in the morning on the due day, or nothing to announce nothing.</param>
/// <param name="DueDayOffsetMinutes">The whole-minute UTC offset the person's due day runs in, which a request stating any lead carries and one stating none may omit.</param>
/// <param name="SourceMessageId">The message the task cites, or <see langword="null" /> where it cites none.</param>
/// <remarks>
/// Bound strictly: a key nothing here binds fails the bind rather than being ignored, so a client that meant to state
/// an origin or a completion is told that this is not where either of them moves instead of having the request read as
/// a rename that silently dropped the rest.
/// <para>
/// A revision states the same document as a creation, and the citation it carries is the one the task already holds:
/// what a person edits is the line, the day, and what announces it, and a request restating the message is writing
/// back what it read. The reminders are part of that record, which is what makes turning the last one off a task
/// stated with none rather than a field left out.
/// </para>
/// <para>
/// The offset travels with the leads because this deployment keeps no timezone for anybody: a day names no instant
/// until a client says which offset it is read in, exactly as a client states the window a day is arranged over. A
/// person who has moved since they set a reminder moves its instant the next time they edit the task.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientTaskRecordRequest(
    string? Title,
    string? DueOn,
    IReadOnlyList<int>? Reminders,
    int? DueDayOffsetMinutes,
    Guid? SourceMessageId);

/// <summary>The completion state a person states for one of their tasks.</summary>
/// <param name="Completed">Whether the task is to stand completed.</param>
/// <remarks>Bound strictly, for the reason the record request is: a key nothing here binds fails the bind rather than being ignored.</remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientTaskCompletionRequest(bool Completed);

/// <summary>One page of what a person owes, soonest due first.</summary>
/// <param name="Tasks">The tasks, soonest due first, with the undated ones last.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
internal sealed record ClientTaskPageResponse(IReadOnlyList<ClientTaskResponse> Tasks, string? NextCursor)
{
    /// <summary>Describes one page on the wire.</summary>
    /// <param name="page">The page that was read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static ClientTaskPageResponse For(PersonalTaskPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ClientTaskPageResponse([.. page.Tasks.Select(ClientTaskResponse.For)], page.NextCursor);
    }
}

/// <summary>One thing a person owes.</summary>
/// <param name="Id">What addresses the task, and what every route naming one names it by.</param>
/// <param name="Title">The line the list is drawn with.</param>
/// <param name="DueOn">The day it is due on as <c>yyyy-mm-dd</c>, or <see langword="null" /> where nobody has said when.</param>
/// <param name="Reminders">The leads it is announced at, in minutes before nine in the morning on the due day, longest first, and empty where nothing announces it.</param>
/// <param name="RemindsAt">The instants those leads fall at, in the same order, which a client draws rather than derives.</param>
/// <param name="Origin">Whether the person committed to it or mail proposed it, as <c>Asserted</c> or <c>Proposed</c>.</param>
/// <param name="Completed">Whether the person has done it.</param>
/// <param name="SourceMessageId">The message the task was read out of or was written beside, or <see langword="null" /> where it cites none.</param>
/// <remarks>
/// It carries what a row draws and stops there: no subject, no body, no address, and no attachment reaches this answer
/// at any size. The citation is an identity rather than a reading of the message, so a client that draws the link
/// follows it over the routes that publish reading mail — and one whose message has since been erased finds nothing
/// there, the task having outlived what it was read out of.
/// <para>
/// The instants the reminders fall at travel beside the leads rather than being left to the client to derive, because
/// the hour a due day is measured back from is a rule this deployment owns: a client computing it would be the second
/// place that rule is written, and the two would disagree the first time one of them changed. The offset those
/// instants were derived in is not published, because a client states its own on every write and never reads one
/// back.
/// </para>
/// </remarks>
internal sealed record ClientTaskResponse(
    Guid Id,
    string Title,
    string? DueOn,
    IReadOnlyList<int> Reminders,
    IReadOnlyList<DateTimeOffset> RemindsAt,
    string Origin,
    bool Completed,
    Guid? SourceMessageId)
{
    /// <summary>Describes one task on the wire.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task" /> is <see langword="null" />.</exception>
    internal static ClientTaskResponse For(PersonalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new ClientTaskResponse(
            task.Id.Value,
            task.Title,
            task.DueOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            [.. task.Reminders.Select(reminder => reminder.MinutesBefore)],
            [.. task.Reminders.Select(task.RemindsAt)],
            task.Origin.ToString(),
            task.IsCompleted,
            task.SourceMessage?.Value);
    }
}

/// <summary>What this deployment does about arranging a day, which is what says whether a screen may offer it.</summary>
/// <param name="ArrangesDays">Whether a day is arranged when somebody asks for one.</param>
/// <remarks>
/// One field and no reason beside it, for the reason the drafting capability carries one: a deployment that declared
/// no chat endpoint is the same answer to a screen as one whose provider is down — arrange the day yourself — and
/// naming which would publish a deployment's configuration to every signed-in browser.
/// </remarks>
internal sealed record ClientDayLayoutAvailabilityResponse(bool ArrangesDays);

/// <summary>The day a person asked to have arranged, as their own client drew it.</summary>
/// <param name="From">The instant their day opens.</param>
/// <param name="Until">The instant it closes.</param>
/// <remarks>
/// The window is stated rather than derived, exactly as it is for reading the calendar and for the same reason: this
/// deployment keeps no timezone for a person, so which hours are their day is something only their client knows.
/// Bound strictly: a key nothing here binds fails the bind rather than being ignored, so a client that meant to state
/// something else about the day is told that nothing else is read.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record ClientDayLayoutRequest(DateTimeOffset? From, DateTimeOffset? Until);

/// <summary>An arrangement of one day, offered and applied to nothing.</summary>
/// <param name="Arranged">Whether an arrangement was composed at all, which is <see langword="false" /> where this deployment composed none.</param>
/// <param name="Placements">What to do and when, earliest first.</param>
/// <param name="NotToday">The tasks that do not realistically fit the day.</param>
/// <remarks>
/// Tasks are named by identity and nothing else, because the client drawing this already holds each row: repeating the
/// lines here would put a second copy of somebody's list on the wire to say something the first copy already says.
/// </remarks>
internal sealed record ClientDayLayoutResponse(
    bool Arranged,
    IReadOnlyList<ClientDayLayoutPlacementResponse> Placements,
    IReadOnlyList<Guid> NotToday)
{
    /// <summary>The answer a deployment that arranged nothing gives.</summary>
    internal static ClientDayLayoutResponse NotArranged { get; } = new(Arranged: false, [], []);

    /// <summary>Describes one arrangement on the wire.</summary>
    /// <param name="suggestion">The arrangement, or <see langword="null" /> where none was composed.</param>
    /// <returns>The response body.</returns>
    internal static ClientDayLayoutResponse For(DayLayoutSuggestion? suggestion) => suggestion is null
        ? NotArranged
        : new ClientDayLayoutResponse(
            Arranged: true,
            [
                .. suggestion.Placements.Select(static placement => new ClientDayLayoutPlacementResponse(
                    placement.Task.Value,
                    placement.StartAt,
                    (int)placement.Duration.TotalMinutes)),
            ],
            [.. suggestion.NotToday.Select(static task => task.Value)]);
}

/// <summary>Where one task is suggested to sit in the day.</summary>
/// <param name="TaskId">The task being placed, as a task row published it.</param>
/// <param name="StartAt">When it is suggested to begin.</param>
/// <param name="Minutes">How long it is suggested to take.</param>
internal sealed record ClientDayLayoutPlacementResponse(Guid TaskId, DateTimeOffset StartAt, int Minutes);

/// <summary>What erasing a task removed.</summary>
/// <param name="Id">The task that was named.</param>
/// <param name="Erased">Whether a task went, which is <see langword="false" /> where this person held none under that identity.</param>
internal sealed record ClientTaskErasureResponse(Guid Id, bool Erased);

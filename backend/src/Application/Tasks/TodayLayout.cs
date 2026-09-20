// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Arranges the signed-in person's day when they ask for it, and changes nothing by doing so.</summary>
/// <remarks>
/// <para>
/// <b>Once per request and never otherwise.</b> Nothing schedules this, no pass reaches it, and opening a screen does
/// not run it: it is the one act behind a control somebody presses, so what a deployment spends on arranging days is
/// what its people asked for.
/// </para>
/// <para>
/// <b>It applies nothing.</b> What comes back is read by the screen that asked and is forgotten unless the person acts
/// on it. No task is rescheduled, no proposal is accepted, and no event is written — every one of those is an act of
/// their own through the route that owns it, and this route holds no write at all.
/// </para>
/// <para>
/// <b>Both halves are read through the readings that own them</b> rather than through queries of this use case's own:
/// the tasks through <see cref="OwnTasks" /> and the day's commitments through <see cref="OwnCalendar" />. Each of
/// those resolves the person from the principal, narrows to what they hold, and decides what a window may ask for, so
/// an arrangement is decided from exactly what the person's own screens would have drawn — and a day cannot be laid
/// out from somebody else's calendar, because there is no way here to name one.
/// </para>
/// <para>
/// <b>Whose day it is says when the day begins.</b> This deployment keeps no timezone for a person, so the window is
/// stated by the client exactly as it states the window it draws the calendar with, and the day a task is compared
/// against is the one the opening instant falls on in the offset it carries.
/// </para>
/// </remarks>
public sealed class TodayLayout
{
    /// <summary>The greatest number of tasks one arrangement is decided from.</summary>
    /// <remarks>
    /// More than any day holds, which is what makes it a bound rather than a rule: a person with a long backlog is
    /// laid out over what is due soonest, and the rest is a list to work down rather than a day to arrange. It also
    /// bounds what one press sends to a provider, which is the other reason a number is needed at all.
    /// </remarks>
    public const int MaximumTasks = 20;

    /// <summary>The greatest number of commitments one arrangement is decided against.</summary>
    /// <remarks>Well past a full day of meetings, and low enough that a window somebody widened cannot compose an unbounded turn.</remarks>
    public const int MaximumCommitments = 50;

    /// <summary>The longest window one request may call a day.</summary>
    /// <remarks>
    /// Two days rather than one, because a day is stated as two instants by a client in a timezone this deployment
    /// does not know, and every offset, every transition, and every rounding a client applies stands well inside it.
    /// It is a bound on what may be asked rather than a definition of a day: what the arrangement treats as the day is
    /// the window as stated.
    /// </remarks>
    public static readonly TimeSpan MaximumDaySpan = TimeSpan.FromDays(2);

    private readonly AccessAuthorization authorization;
    private readonly OwnTasks tasks;
    private readonly OwnCalendar calendar;
    private readonly IDayLayoutPlanner planner;
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the person it acts for.</param>
    /// <param name="tasks">Reads what the person owes, under the reading that owns their list.</param>
    /// <param name="calendar">Reads what the person is already committed to, under the reading that owns their calendar.</param>
    /// <param name="planner">Arranges the day, in whichever state the deployment left it.</param>
    /// <param name="egressGuard">Holds the posture the lines are scanned under while the arrangement is asked for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public TodayLayout(
        AccessAuthorization authorization,
        OwnTasks tasks,
        OwnCalendar calendar,
        IDayLayoutPlanner planner,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.authorization = authorization;
        this.tasks = tasks;
        this.calendar = calendar;
        this.planner = planner;
        this.egressGuard = egressGuard;
    }

    /// <summary>Suggests how the signed-in person's day could be arranged.</summary>
    /// <param name="dayStart">The instant their day opens.</param>
    /// <param name="dayEnd">The instant it closes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The arrangement or the reason there is none, or <see langword="null" /> where what was asked for is not a day this deployment arranges.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The deployment's state is asked first, so an instance that lays out no day reads nobody's tasks and nobody's
    /// calendar to say so. What is laid out is what the person owes and has not done by that day — a task they have
    /// already completed is not work the day has to hold, and one mail proposed and nobody accepted is not work they
    /// owe at all, which is why only the committed half is read.
    /// </remarks>
    public async Task<DayLayoutDerivation?> SuggestAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        var user = this.authorization.RequireUser();

        if (dayEnd <= dayStart || dayEnd - dayStart > MaximumDaySpan)
        {
            return null;
        }

        if (!this.planner.IsActive)
        {
            return DayLayoutDerivation.Withholding(DayLayoutWithholding.NotActivated);
        }

        var owed = await this.ReadOwedAsync(dayStart, cancellationToken);
        var committed = await this.calendar.ReadWindowAsync(
            dayStart,
            dayEnd,
            CalendarEventOrigin.Asserted,
            MaximumCommitments,
            cancellationToken);

        if (committed is null)
        {
            return null;
        }

        // Established around the one call that leaves this deployment, and against the person rather than an account,
        // because a task and an event are held per person and a line on either may have been read out of any mailbox
        // they are assigned.
        using var actingFor = this.egressGuard.ActingFor(user);

        return await this.planner.SuggestAsync(
            new DayLayoutQuestion(
                dayStart,
                dayEnd,
                owed,
                [.. committed.Select(static held => new DayLayoutCommitment(held.Title.Value, held.Start, held.End))]),
            cancellationToken);
    }

    /// <summary>Reads what the person owes by the end of the day being arranged, soonest due first.</summary>
    /// <remarks>
    /// One page, because the list is read soonest due first with the undated last: everything due by that day stands
    /// at the front of it, and a page of the size this reads is far past what a day could hold. A task with no day on
    /// it is not part of any particular day and is left where it is, so an arrangement never quietly decides that
    /// something nobody dated is due now.
    /// </remarks>
    private async Task<IReadOnlyList<DayLayoutTask>> ReadOwedAsync(
        DateTimeOffset dayStart,
        CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(dayStart.DateTime);
        var page = await this.tasks.ReadPageAsync(
            PersonalTaskOrigin.Asserted,
            OwnTasks.MaximumPageSize,
            cursor: null,
            cancellationToken);

        return page is null
            ? []
            :
            [
                .. page.Tasks
                    .Where(task => !task.IsCompleted && task.DueOn is { } due && due <= day)
                    .Take(MaximumTasks)
                    .Select(static task => new DayLayoutTask(task.Id, task.Title, task.DueOn)),
            ];
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.Notifications;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Notifications;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Reminders;

/// <summary>Announces the reminders that have come due, of every kind, one bounded pass at a time.</summary>
/// <remarks>
/// <para>
/// <b>A reminder is due whether or not anybody has a client open</b>, which is the whole reason the producer is here
/// rather than in a screen: what raises it is this pass writing a notification record, and every client — the one
/// that was open, the one opened an hour later, and the second machine — learns about it the way it learns about
/// everything else.
/// </para>
/// <para>
/// <b>One producer for every kind of reminder rather than one per kind.</b> A calendar event and a task's due date
/// are different records with different anchors, and nothing else about announcing them differs: the ordering that
/// makes it exactly once, the bound on a pass, the window that keeps a long outage quiet, and the lease are one
/// decision each. So each kind contributes an <see cref="IReminderSchedule" /> that knows its own table, and this
/// pass reads every one of them registered.
/// </para>
/// <para>
/// The pass is one run for the whole deployment, so it runs only under the lease on its own scope, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides. A replica refused the lease announces nothing and asks again on its next interval. The scope is the
/// reminders' rather than the calendar's, so a rolling upgrade past the build that renamed it has one replica
/// holding each — which announces nothing twice, because the claim is conditional on the instant and the
/// deduplication key names the reminder.
/// </para>
/// <para>
/// <b>Exactly once, from two rules rather than one.</b> The notification is written first and the claim recorded
/// after it, so a pass that ends between them announces nothing twice — the record's own deduplication key names the
/// reminder and the instant it falls at, so the repeat is folded into the statement already standing unread while a
/// reminder that has become due at a new instant is a condition of its own and is said — and loses nothing either,
/// because the reminder is still unclaimed and the next pass reaches it. What the claim is made against is that same
/// instant, which is what makes a record moved forward due again at its new time and one moved back onto an
/// announced time stay quiet.
/// </para>
/// <para>
/// <b>Nothing long overdue is announced.</b> A deployment that was off for a day comes back to reminders nobody could
/// have acted on, about things that have already happened, and delivering them would be a burst of statements in
/// place of the one thing somebody wanted to be told. <see cref="LongestLateAnnouncement" /> is how late is still
/// worth saying; anything older is left where it is and announced by nothing.
/// </para>
/// </remarks>
public sealed class ReminderSweep
{
    /// <summary>The lease the pass is held under, which is the deployment's as the pass is.</summary>
    internal static readonly WorkScope SweepScope = WorkScope.Create("reminders");

    /// <summary>How many reminders one pass announces at most, per kind of thing that carries them.</summary>
    /// <remarks>
    /// Each one is a notification written and a client told, so the bound is what keeps a deployment whose calendars
    /// all name the same hour from spending a pass on every one of them; what it does not reach is due on the next.
    /// It is per kind rather than across them so that a backlog of one cannot starve another.
    /// </remarks>
    public const int MaximumRemindersPerRun = 200;

    /// <summary>How late a reminder may still be announced, past which it is passed over in silence.</summary>
    public static readonly TimeSpan LongestLateAnnouncement = TimeSpan.FromHours(1);

    private readonly AccessAuthorization authorization;
    private readonly IReadOnlyList<IReminderSchedule> schedules;
    private readonly NotificationRaiser raiser;
    private readonly IWorkLeaseRunner leases;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass.</summary>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <param name="schedules">Name what has come due, one per kind of thing that carries reminders, and record what was announced.</param>
    /// <param name="raiser">Writes each notification and tells whatever the person has open.</param>
    /// <param name="leases">Holds the pass's scope for the length of one run.</param>
    /// <param name="timeProvider">Reads the instant a reminder is judged due against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ReminderSweep(
        AccessAuthorization authorization,
        IEnumerable<IReminderSchedule> schedules,
        NotificationRaiser raiser,
        IWorkLeaseRunner leases,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(raiser);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.schedules = [.. schedules];
        this.raiser = raiser;
        this.leases = leases;
        this.timeProvider = timeProvider;
    }

    /// <summary>Runs one bounded pass if this replica takes the pass's lease.</summary>
    /// <param name="cancellationToken">Cancels the claim and the pass.</param>
    /// <returns>How many reminders the pass announced, or <see langword="null" /> when another replica holds the pass.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    public async Task<int?> RunAsync(CancellationToken cancellationToken)
    {
        this.authorization.RequireProcessIdentity();

        var announced = 0;

        var ran = await this.leases.TryRunUnderLeaseAsync(
            SweepScope,
            async heldToken => announced = await this.AnnounceAsync(heldToken),
            cancellationToken);

        return ran ? announced : null;
    }

    private async Task<int> AnnounceAsync(CancellationToken cancellationToken)
    {
        var asOf = this.timeProvider.GetUtcNow();
        var announced = 0;

        foreach (var schedule in this.schedules)
        {
            announced += await this.AnnounceAsync(schedule, asOf, cancellationToken);
        }

        return announced;
    }

    private async Task<int> AnnounceAsync(
        IReminderSchedule schedule,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var due = await schedule.ReadDueAsync(
            asOf,
            asOf - LongestLateAnnouncement,
            MaximumRemindersPerRun,
            cancellationToken);

        var announced = 0;

        foreach (var reminder in due)
        {
            await this.raiser.RecordAndAnnounceAsync(Composed(reminder), cancellationToken);

            if (await schedule.MarkRaisedAsync(reminder, cancellationToken))
            {
                announced++;
            }
        }

        return announced;
    }

    /// <summary>Composes the notification one due reminder is said in.</summary>
    /// <remarks>
    /// The headline is what the thing is called, because that is what somebody being reminded needs to read first and
    /// there is nothing else a reminder is about. The two lines are the service's own English, for a reader with no
    /// client to say it in their own; the statement beside them is the condition and the lead, which is what a client
    /// draws the row from in the language its reader has.
    /// </remarks>
    private static Notification Composed(DueReminder due) =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(due.DueAt)),
            due.Owner,
            Kind(due.Subject),
            due.Headline,
            Remaining(due.Reminder.MinutesBefore),
            Said(due.Subject, due.Reminder.MinutesBefore),
            source: null,
            Leading(due.Subject, due.Identity),
            // The reminder *at the instant it falls at*, which is what makes the key fold the repeat it is for and
            // nothing else: a pass that wrote the row and then ended before recording the claim comes back to the
            // same instant and folds into the statement already standing unread, while a reminder the record's move
            // made due again is a different condition and is said at the new time even where the old statement has
            // not been read. Leaving the instant out would claim that second reminder against a row nothing wrote,
            // and the person would never be told. All three halves are MailFathom's own — an identity it issued, a
            // number somebody chose, and an instant it derived — so the key carries nothing of what the thing is
            // about.
            NotificationDeduplicationKey.For(
                Deduplicates(due.Subject),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{due.Identity}:{due.Reminder.MinutesBefore}:{due.DueAt:O}")),
            due.DueAt);

    /// <summary>Says what part of MailFathom a reminder of one kind is about.</summary>
    private static NotificationKind Kind(ReminderSubject subject) => subject switch
    {
        ReminderSubject.CalendarEvent => NotificationKind.Calendar,
        ReminderSubject.PersonalTask => NotificationKind.Task,
        _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "A reminder is about a declared kind of record."),
    };

    /// <summary>States the condition, which is the same lead said about a different kind of record.</summary>
    private static NotificationStatement Said(ReminderSubject subject, int minutesBefore) => subject switch
    {
        ReminderSubject.CalendarEvent => NotificationStatement.CalendarReminderDue(minutesBefore),
        ReminderSubject.PersonalTask => NotificationStatement.TaskReminderDue(minutesBefore),
        _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "A reminder is about a declared kind of record."),
    };

    /// <summary>Says where opening the notification leads, which is the record the reminder is about.</summary>
    private static NotificationTarget Leading(ReminderSubject subject, Guid identity) => subject switch
    {
        ReminderSubject.CalendarEvent => NotificationTarget.ToCalendarEvent(CalendarEventId.Create(identity)),
        ReminderSubject.PersonalTask => NotificationTarget.ToPersonalTask(PersonalTaskId.Create(identity)),
        _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "A reminder is about a declared kind of record."),
    };

    /// <summary>Names the deduplication key's own subject, which keeps two kinds of reminder from folding into each other.</summary>
    /// <remarks>
    /// An identity is unique within its kind rather than across the deployment, so a key naming only the identifier
    /// would let a task and an event that happened to share one collapse into a single statement.
    /// </remarks>
    private static string Deduplicates(ReminderSubject subject) => subject switch
    {
        ReminderSubject.CalendarEvent => "calendar-reminder",
        ReminderSubject.PersonalTask => "task-reminder",
        _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, "A reminder is about a declared kind of record."),
    };

    /// <summary>Says how long is left in the coarsest whole unit that states the lead exactly.</summary>
    /// <remarks>
    /// The same reading the client's own reminder labels take, so the English fallback and the sentence somebody
    /// sees in their own language describe one lead rather than two. A lead that divides into no coarser unit stays
    /// in minutes rather than being rounded into one: a day and a half is thirty-six hours, and a day and half an
    /// hour is neither a whole number of hours nor of days and is therefore said in minutes. The same words whatever
    /// the reminder is about, which is what the design draws: which kind of record it is is what the headline and
    /// the statement carry, and a second wording of <i>now</i> here would be a second answer to how long is left.
    /// </remarks>
    private static string Remaining(int minutesBefore) => minutesBefore switch
    {
        0 => "Starting now.",
        _ when minutesBefore % (24 * 60) == 0 => Counted(minutesBefore / (24 * 60), "day"),
        _ when minutesBefore % 60 == 0 => Counted(minutesBefore / 60, "hour"),
        _ => Counted(minutesBefore, "minute"),
    };

    private static string Counted(int count, string unit) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count} {unit}{(count == 1 ? string.Empty : "s")} left.");
}

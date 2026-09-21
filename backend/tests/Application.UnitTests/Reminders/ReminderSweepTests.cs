// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.Notifications;
using MailFathom.Application.Reminders;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Notifications;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Reminders;

/// <summary>
/// Covers the pass that announces reminders. What it has to hold is that a reminder is announced exactly once however
/// often the pass runs, that a record moved to a new time becomes due again there and one moved back onto an
/// announced time stays quiet, that a reminder nobody could still act on is passed over rather than delivered in a
/// burst, that a replica refused the lease announces nothing, and that every kind of reminder is announced by this
/// one pass in the shape its own kind takes.
/// </summary>
public sealed class ReminderSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Standup = new("0197a3c0-0000-7000-8000-000000000001");

    private static readonly Guid CounterProposal = new("0197a3c0-0000-7000-8000-000000000002");

    private readonly StubReminderSchedule schedule = new();
    private readonly StubReminderSchedule tasks = new();
    private readonly InMemoryNotificationStore notifications = new();

    [Fact]
    public async Task RunAsync_AReminderThatHasComeDue_AnnouncesItOnceAndLeadsToItsEvent()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));

        // Act
        var announced = await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, announced);

        var raised = Assert.Single(this.notifications.Recorded);
        Assert.Equal(SyntheticMailUser.Deployment, raised.User);
        Assert.Equal(NotificationKind.Calendar, raised.Kind);
        Assert.Equal("Standup", raised.Title);
        Assert.Equal(NotificationCause.CalendarReminderDue, raised.Statement!.Cause);
        Assert.Equal(15, raised.Statement.Counted);
        Assert.Equal(CalendarEventId.Create(Standup), raised.Target.CalendarEvent);
    }

    /// <summary>
    /// The whole of what stage 10 adds: the same pass, the same ordering, and a notification of the task kind that
    /// leads to the task rather than to an event.
    /// </summary>
    [Fact]
    public async Task RunAsync_ATaskReminderThatHasComeDue_AnnouncesItAsATaskAndLeadsToIt()
    {
        // Arrange
        this.tasks.Holding(DueTask(Now.AddMinutes(-1), minutesBefore: 240));

        // Act
        var announced = await this.SweepingBoth().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, announced);

        var raised = Assert.Single(this.notifications.Recorded);
        Assert.Equal(NotificationKind.Task, raised.Kind);
        Assert.Equal("Send the counter-proposal", raised.Title);
        Assert.Equal(NotificationCause.TaskReminderDue, raised.Statement!.Cause);
        Assert.Equal(240, raised.Statement.Counted);
        Assert.Equal(PersonalTaskId.Create(CounterProposal), raised.Target.PersonalTask);
    }

    /// <summary>One pass over every kind rather than one pass per kind, which is why there is a single worker.</summary>
    [Fact]
    public async Task RunAsync_RemindersOfBothKinds_AnnouncesEveryOneInOnePass()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        this.tasks.Holding(DueTask(Now.AddMinutes(-1), minutesBefore: 240));

        // Act
        var announced = await this.SweepingBoth().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, announced);
        Assert.Equal(
            [NotificationKind.Calendar, NotificationKind.Task],
            this.notifications.Recorded.Select(raised => raised.Kind));
    }

    /// <summary>
    /// An identity is unique within its kind rather than across the deployment, so the key has to name the kind as
    /// well — otherwise a task and an event sharing one would fold into a single statement and one of them would
    /// never be said.
    /// </summary>
    [Fact]
    public async Task RunAsync_ATaskAndAnEventUnderOneIdentity_AnnouncesBoth()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        this.tasks.Holding(DueTask(Now.AddMinutes(-1), minutesBefore: 15) with { Identity = Standup });

        // Act
        await this.SweepingBoth().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, this.notifications.Recorded.Count);
    }

    /// <summary>The whole reason the claim is recorded: a pass that runs every minute says one thing once.</summary>
    [Fact]
    public async Task RunAsync_APassThatAlreadyAnnouncedAReminder_SaysNothingASecondTime()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        var sweep = this.Sweeping();

        // Act
        await sweep.RunAsync(TestContext.Current.CancellationToken);
        var announcedAgain = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, announcedAgain);
        Assert.Single(this.notifications.Recorded);
    }

    /// <summary>
    /// A pass that wrote the row and ended before recording the claim announces nothing twice, because the key names
    /// the reminder rather than the occasion — and loses nothing, because the reminder is still unclaimed.
    /// </summary>
    [Fact]
    public async Task RunAsync_APassThatEndedBetweenTheRowAndTheClaim_FoldsTheRepeatAndStillRecordsTheClaim()
    {
        // Arrange
        var due = Due(Now.AddMinutes(-1), minutesBefore: 15);
        this.schedule.Holding(due);
        await new NotificationRaiser(this.notifications, ClientSignalPublishers.ReachingNobody)
            .RecordAndAnnounceAsync(AlreadyStanding(due), TestContext.Current.CancellationToken);

        // Act
        var announced = await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, announced);
        Assert.Single(this.notifications.Recorded);
        Assert.True(this.schedule.IsClaimed(Standup, minutesBefore: 15));
    }

    /// <summary>
    /// What the claim is made against is the instant, so a reminder on a moved event comes due at the new one — and
    /// it is said there even where the statement about the old time is still standing unread, which is the case the
    /// key has to tell apart from a pass repeating itself. A key naming only the event and the lead would fold the
    /// second statement into the first, write nothing, and then claim the reminder anyway, so the person would never
    /// be told about the time the event actually moved to.
    /// </summary>
    [Fact]
    public async Task RunAsync_AnEventMovedWhileItsFirstReminderWasStillUnread_AnnouncesItAgainAtTheNewTime()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        var clock = new FakeTimeProvider(Now);
        var sweep = this.Sweeping(clock);

        await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Act
        clock.Advance(TimeSpan.FromMinutes(20));
        this.schedule.Moved(Standup, minutesBefore: 15, clock.GetUtcNow().AddMinutes(-1));
        var announced = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, announced);
        Assert.Equal(2, this.notifications.Recorded.Count);
    }

    /// <summary>A reminder about an event that has already happened is one nobody could act on, so it is left alone.</summary>
    [Fact]
    public async Task RunAsync_AReminderLongerOverdueThanIsWorthSaying_IsPassedOverInSilence()
    {
        // Arrange
        this.schedule.Holding(Due(Now - ReminderSweep.LongestLateAnnouncement.Add(TimeSpan.FromMinutes(1)), 15));

        // Act
        var announced = await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, announced);
        Assert.Empty(this.notifications.Recorded);
    }

    /// <summary>A lead that has not fallen yet is a reminder the next pass reaches rather than one this pass may say.</summary>
    [Fact]
    public async Task RunAsync_AReminderThatHasNotFallenYet_IsNotAnnounced()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(1), minutesBefore: 15));

        // Act
        var announced = await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, announced);
        Assert.Empty(this.notifications.Recorded);
    }

    /// <summary>A deleted event takes its reminders with it, so the pass has nothing to say about one.</summary>
    [Fact]
    public async Task RunAsync_AnEventDeletedBeforeItsReminderFell_ProducesNothing()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        this.schedule.Deleted(Standup);

        // Act
        var announced = await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, announced);
        Assert.Empty(this.notifications.Recorded);
    }

    /// <summary>The bound is what keeps a deployment whose calendars all name one hour from spending a pass on it.</summary>
    [Fact]
    public async Task RunAsync_Always_AsksForNoMoreThanOnePassMayAnnounce()
    {
        // Arrange
        var schedule = Substitute.For<IReminderSchedule>();
        schedule
            .ReadDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        // Act
        await this.Sweeping(new FakeTimeProvider(Now), schedule).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        await schedule.Received(1).ReadDueAsync(
            Now,
            Now - ReminderSweep.LongestLateAnnouncement,
            ReminderSweep.MaximumRemindersPerRun,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AReplicaRefusedTheLease_AnnouncesNothingAndSaysSo()
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore: 15));
        var leases = Substitute.For<IWorkLeaseRunner>();
        leases
            .TryRunUnderLeaseAsync(Arg.Any<WorkScope>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var announced = await this.Sweeping(leases: leases).RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(announced);
        Assert.Empty(this.notifications.Recorded);
    }

    /// <summary>The pass reads every calendar in the deployment, so nothing but the deployment's own process runs it.</summary>
    [Fact]
    public async Task RunAsync_ACallerRatherThanTheProcess_IsRefused()
    {
        // Arrange
        var sweep = new ReminderSweep(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            [this.schedule],
            new NotificationRaiser(this.notifications, ClientSignalPublishers.ReachingNobody),
            new GrantingLeaseRunner(),
            new FakeTimeProvider(Now));

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => sweep.RunAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>What a reader with no client is told, which is the same lead the statement carries.</summary>
    [Theory]
    [InlineData(0, "Starting now.")]
    [InlineData(1, "1 minute left.")]
    [InlineData(15, "15 minutes left.")]
    [InlineData(60, "1 hour left.")]
    [InlineData(90, "90 minutes left.")]
    [InlineData(24 * 60, "1 day left.")]
    [InlineData(2 * 24 * 60, "2 days left.")]
    [InlineData(36 * 60, "36 hours left.")]
    [InlineData(24 * 60 + 30, "1470 minutes left.")]
    public async Task RunAsync_AReminderAtEachLead_SaysWhatIsLeftInTheCoarsestWholeUnit(int minutesBefore, string said)
    {
        // Arrange
        this.schedule.Holding(Due(Now.AddMinutes(-1), minutesBefore));

        // Act
        await this.Sweeping().RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(said, Assert.Single(this.notifications.Recorded).Body);
    }

    private static DueReminder Due(DateTimeOffset dueAt, int minutesBefore) => new(
        SyntheticMailUser.Deployment,
        ReminderSubject.CalendarEvent,
        Standup,
        "Standup",
        Reminder.Create(minutesBefore),
        dueAt);

    private static DueReminder DueTask(DateTimeOffset dueAt, int minutesBefore) => new(
        SyntheticMailUser.Deployment,
        ReminderSubject.PersonalTask,
        CounterProposal,
        "Send the counter-proposal",
        Reminder.Create(minutesBefore),
        dueAt);

    /// <summary>The row a pass would have written before it ended, which is what the repeat is folded into.</summary>
    private static Notification AlreadyStanding(DueReminder due) => Notification.Compose(
        NotificationId.Create(Guid.CreateVersion7(due.DueAt)),
        due.Owner,
        NotificationKind.Calendar,
        due.Headline,
        "15 minutes left.",
        NotificationStatement.CalendarReminderDue(due.Reminder.MinutesBefore),
        source: null,
        NotificationTarget.ToCalendarEvent(CalendarEventId.Create(due.Identity)),
        NotificationDeduplicationKey.For(
            "calendar-reminder",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{due.Identity}:{due.Reminder.MinutesBefore}:{due.DueAt:O}")),
        due.DueAt);

    private ReminderSweep Sweeping(
        FakeTimeProvider? clock = null,
        IReminderSchedule? schedule = null,
        IWorkLeaseRunner? leases = null) =>
        this.SweepingOver([schedule ?? (IReminderSchedule)this.schedule], clock, leases);

    /// <summary>The pass as the deployment composes it, holding the calendar's schedule and the task list's.</summary>
    private ReminderSweep SweepingBoth() => this.SweepingOver([this.schedule, this.tasks]);

    private ReminderSweep SweepingOver(
        IReadOnlyList<IReminderSchedule> schedules,
        FakeTimeProvider? clock = null,
        IWorkLeaseRunner? leases = null) =>
        new(
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            schedules,
            new NotificationRaiser(this.notifications, ClientSignalPublishers.ReachingNobody),
            leases ?? new GrantingLeaseRunner(),
            clock ?? new FakeTimeProvider(Now));

    /// <summary>Stands in for the lease table on a replica nothing competes with.</summary>
    private sealed class GrantingLeaseRunner : IWorkLeaseRunner
    {
        public async Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            await work(cancellationToken);

            return true;
        }
    }

    /// <summary>
    /// Stands in for the reminder rows, under the same two rules the persisted schedule publishes: what is read is
    /// what has fallen and is unclaimed, and a claim is recorded only against the instant the reminder still falls at.
    /// </summary>
    /// <remarks>
    /// Restated here rather than substituted away because the pass's exactly-once behaviour is written against them —
    /// a double that claimed unconditionally would pass a sweep that announced a moved event twice. What it does not
    /// model is the race two replicas settle on the database, which the integration suite proves.
    /// </remarks>
    private sealed class StubReminderSchedule : IReminderSchedule
    {
        private readonly List<StandingReminder> standing = [];

        public void Holding(DueReminder due) => this.standing.Add(new StandingReminder(due));

        public void Deleted(Guid identity) =>
            this.standing.RemoveAll(reminder => reminder.Due.Identity == identity);

        public void Moved(Guid identity, int minutesBefore, DateTimeOffset dueAt)
        {
            foreach (var reminder in this.Matching(identity, minutesBefore))
            {
                reminder.Due = reminder.Due with { DueAt = dueAt };
                reminder.RaisedForDueAt = null;
            }
        }

        public bool IsClaimed(Guid identity, int minutesBefore) =>
            this.Matching(identity, minutesBefore).All(reminder => reminder.RaisedForDueAt is not null);

        public Task<IReadOnlyList<DueReminder>> ReadDueAsync(
            DateTimeOffset asOf,
            DateTimeOffset notDueBefore,
            int limit,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

            return Task.FromResult<IReadOnlyList<DueReminder>>(
            [
                .. this.standing
                    .Where(reminder => reminder.RaisedForDueAt is null
                        && reminder.Due.DueAt <= asOf
                        && reminder.Due.DueAt >= notDueBefore)
                    .OrderBy(reminder => reminder.Due.DueAt)
                    .Take(limit)
                    .Select(reminder => reminder.Due),
            ]);
        }

        public Task<bool> MarkRaisedAsync(DueReminder due, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(due);

            var held = this.standing.SingleOrDefault(reminder => reminder.Due.Identity == due.Identity
                && reminder.Due.Reminder == due.Reminder
                && reminder.Due.DueAt == due.DueAt
                && reminder.RaisedForDueAt is null);

            if (held is null)
            {
                return Task.FromResult(false);
            }

            held.RaisedForDueAt = due.DueAt;

            return Task.FromResult(true);
        }

        private IEnumerable<StandingReminder> Matching(Guid identity, int minutesBefore) =>
            this.standing.Where(reminder => reminder.Due.Identity == identity
                && reminder.Due.Reminder.MinutesBefore == minutesBefore);

        private sealed class StandingReminder(DueReminder due)
        {
            public DueReminder Due { get; set; } = due;

            public DateTimeOffset? RaisedForDueAt { get; set; }
        }
    }
}

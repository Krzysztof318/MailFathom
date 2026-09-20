// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.Coordination;
using MailFathom.Application.Notifications;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Notifications;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that announces the calendar reminders that have come due, one pass per interval.</summary>
/// <remarks>
/// What the worker owns is the interval, the isolation, and what a pass is allowed to say about itself — not what a
/// pass decides about a reminder, which is asserted where the sweep lives. A loop that ended on the first failed pass
/// would leave every later reminder unannounced for as long as the replica ran.
/// </remarks>
public sealed class CalendarReminderWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a pass actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly CalendarEventId Standup =
        CalendarEventId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));

    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_AnnouncesNothing()
    {
        // Arrange
        var schedule = Substitute.For<ICalendarReminderSchedule>();
        schedule
            .ReadDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        using var worker = CreateWorker(schedule, out _, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await schedule.DidNotReceive().ReadDueAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A pass is reported as a count, and neither the person nor what their event is called reaches the log.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatAnnouncedAReminder_ReportsTheCountWithoutNamingTheEvent()
    {
        // Arrange
        var claimed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedule = Substitute.For<ICalendarReminderSchedule>();
        schedule
            .ReadDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Due()]);
        schedule.MarkRaisedAsync(Arg.Any<DueCalendarReminder>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                claimed.TrySetResult();

                return true;
            });
        using var worker = CreateWorker(schedule, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, claimed.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var reported = Assert.Single(logger.Messages);
        Assert.Contains("Announced 1 calendar reminders", reported, StringComparison.Ordinal);
        Assert.DoesNotContain("Design review", reported, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Standup.ToString(), reported, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A failed pass is reported and the worker stays alive, so a later interval says what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItAndAsksAgainOnALaterInterval()
    {
        // Arrange
        var secondPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passes = 0;
        var schedule = Substitute.For<ICalendarReminderSchedule>();
        schedule
            .ReadDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DueCalendarReminder>>(_ =>
            {
                if (Interlocked.Increment(ref passes) >= 2)
                {
                    secondPass.TrySetResult();
                }

                throw new InvalidOperationException("the database is unavailable");
            });
        using var worker = CreateWorker(schedule, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, secondPass.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("pass over the calendar reminders that had come due failed", StringComparison.Ordinal));
    }

    private static DueCalendarReminder Due() => new(
        SyntheticMailUser.Deployment,
        Standup,
        CalendarEventTitle.Create("Design review"),
        CalendarReminder.Create(15),
        new DateTimeOffset(2026, 9, 21, 8, 45, 0, TimeSpan.Zero));

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the pass before it
    /// returns, and an advance that arrives before that wait exists is simply lost.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Task reached)
    {
        const int advanceAttempts = 200;
        var passObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached.IsCompleted; attempt++)
        {
            timeProvider.Advance(CalendarReminderWorker.Interval);

            await Task.WhenAny(reached, Task.Delay(passObservationWindow, TestContext.Current.CancellationToken));
        }

        await reached.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    /// <summary>Composes the worker over the real sweep, a substituted schedule, and a lease nothing competes for.</summary>
    private static CalendarReminderWorker CreateWorker(
        ICalendarReminderSchedule schedule,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<CalendarReminderWorker> logger)
    {
        logger = new RecordingLogger<CalendarReminderWorker>();
        timeProvider = new FakeTimeProvider();

        var notifications = Substitute.For<INotificationStore>();
        notifications.RecordAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(schedule);
        services.AddSingleton(notifications);
        services.AddSingleton(ClientSignalPublishers.ReachingNobody);
        services.AddSingleton<IWorkLeaseRunner>(new GrantingLeaseRunner());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddScoped<NotificationRaiser>();
        services.AddScoped<CalendarReminderSweep>();

        var serviceProvider = services.BuildServiceProvider();

        return new CalendarReminderWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            timeProvider);
    }

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
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>
/// Covers what a day is arranged from and what asking for one leaves behind. The arrangement itself is the planner's
/// and is measured where a model can be asked; what is asserted here is which tasks and which commitments reach it,
/// which windows are refused, and that nothing is written by asking.
/// </summary>
public sealed class TodayLayoutTests
{
    private static readonly MailUserId Person = SyntheticMailUser.Deployment;

    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DayEnd = new(2026, 9, 21, 20, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Day = new(2026, 9, 21);

    [Fact]
    public async Task SuggestAsync_ADayWithTasksOwedOnIt_PutsThemAndTheDaysCommitmentsToThePlanner()
    {
        // Arrange
        DayLayoutQuestion? asked = null;
        var planner = PlannerAnswering(question =>
        {
            asked = question;

            return DayLayoutDerivation.Settled(
                new DayLayoutSuggestion(
                    [new DayLayoutPlacement(question.Tasks[0].Id, DayStart.AddHours(2), TimeSpan.FromMinutes(45))],
                    []));
        });

        var layout = Compose(
            planner,
            owed: [Owed("Answer the supplier", Day)],
            committed: [Committed("Standup", DayStart.AddHours(1))]);

        // Act
        var derivation = await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(asked);
        Assert.Equal(DayStart, asked.DayStart);
        Assert.Equal(DayEnd, asked.DayEnd);
        Assert.Equal("Answer the supplier", Assert.Single(asked.Tasks).Title);
        Assert.Equal("Standup", Assert.Single(asked.Commitments).Title);
        Assert.Single(derivation!.Suggestion!.Placements);
    }

    /// <summary>What reaches the arrangement is what is owed by that day, done or dated otherwise is not the day's work.</summary>
    [Fact]
    public async Task SuggestAsync_ATaskCompletedDatedLaterOrUndated_LeavesItOutOfTheDay()
    {
        // Arrange
        DayLayoutQuestion? asked = null;
        var planner = PlannerAnswering(question =>
        {
            asked = question;

            return DayLayoutDerivation.Settled(new DayLayoutSuggestion([], []));
        });

        var layout = Compose(
            planner,
            owed:
            [
                Owed("Answer the supplier", Day),
                Owed("Overdue since yesterday", Day.AddDays(-1)),
                Owed("Next week's report", Day.AddDays(7)),
                Owed("Undated", dueOn: null),
                Owed("Already done", Day, isCompleted: true),
            ],
            committed: []);

        // Act
        await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(asked);
        Assert.Equal(
            ["Answer the supplier", "Overdue since yesterday"],
            asked.Tasks.Select(task => task.Title));
    }

    /// <summary>A day this deployment does not arrange is reported rather than composed, and nothing is read to say so.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(72)]
    public async Task SuggestAsync_AWindowThatIsNotADay_RefusesItWithoutAskingAnybody(int hours)
    {
        // Arrange
        var planner = PlannerAnswering(_ => DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));
        var layout = Compose(planner, owed: [Owed("Answer the supplier", Day)], committed: []);

        // Act
        var derivation = await layout.SuggestAsync(
            DayStart,
            DayStart.AddHours(hours),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(derivation);
        await planner.DidNotReceiveWithAnyArgs().SuggestAsync(
            Arg.Any<DayLayoutQuestion>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An instance that arranges no day reads nobody's list and nobody's calendar to say so.</summary>
    [Fact]
    public async Task SuggestAsync_ADeploymentThatArrangesNoDay_WithholdsWithoutReadingAnything()
    {
        // Arrange
        var taskStore = Substitute.For<IPersonalTaskStore>();
        var planner = Substitute.For<IDayLayoutPlanner>();
        planner.IsActive.Returns(false);

        var layout = Compose(planner, owed: [], committed: [], taskStore: taskStore);

        // Act
        var derivation = await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DayLayoutWithholding.NotActivated, derivation!.Withheld);
        await taskStore.DidNotReceiveWithAnyArgs().ReadAsync(
            default,
            default,
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    /// <summary>Asking for an arrangement is a reading, so the list it was decided from is the list it leaves behind.</summary>
    [Fact]
    public async Task SuggestAsync_AnArrangementOffered_WritesNothingToTheList()
    {
        // Arrange
        var taskStore = TaskStoreHolding([Owed("Answer the supplier", Day)]);
        var planner = PlannerAnswering(_ => DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));
        var layout = Compose(planner, owed: [], committed: [], taskStore: taskStore);

        // Act
        await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        await taskStore.DidNotReceiveWithAnyArgs().AddAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>());
        await taskStore.DidNotReceiveWithAnyArgs().ReviseAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>());
        await taskStore.DidNotReceiveWithAnyArgs().AcceptAsync(
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    /// <summary>The proposals are not work the person owes, so a day is arranged out of what they committed to.</summary>
    [Fact]
    public async Task SuggestAsync_ADayWithProposalsOnTheList_ReadsOnlyWhatThePersonCommittedTo()
    {
        // Arrange
        var taskStore = TaskStoreHolding([Owed("Answer the supplier", Day)]);
        var planner = PlannerAnswering(_ => DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));
        var layout = Compose(planner, owed: [], committed: [], taskStore: taskStore);

        // Act
        await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        await taskStore.Received(1).ReadAsync(
            Person,
            PersonalTaskOrigin.Asserted,
            null,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>What is already on the calendar is read as the shape of the day, and a proposal is not on it yet.</summary>
    [Fact]
    public async Task SuggestAsync_ADayWithProposedEvents_ReadsOnlyTheCalendarItself()
    {
        // Arrange
        var calendarStore = Substitute.For<ICalendarEventStore>();
        calendarStore
            .ReadRangeAsync(Person, Arg.Any<CalendarEventQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => []);

        var planner = PlannerAnswering(_ => DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));
        var layout = Compose(
            planner,
            owed: [Owed("Answer the supplier", Day)],
            committed: [],
            calendarStore: calendarStore);

        // Act
        await layout.SuggestAsync(DayStart, DayEnd, TestContext.Current.CancellationToken);

        // Assert
        await calendarStore.Received(1).ReadRangeAsync(
            Person,
            Arg.Is<CalendarEventQuery>(query =>
                query!.Origin == CalendarEventOrigin.Asserted
                && query.From == DayStart
                && query.Until == DayEnd),
            Arg.Any<CancellationToken>());
    }

    private static IDayLayoutPlanner PlannerAnswering(Func<DayLayoutQuestion, DayLayoutDerivation> answer)
    {
        var planner = Substitute.For<IDayLayoutPlanner>();
        planner.IsActive.Returns(true);
        planner
            .SuggestAsync(Arg.Any<DayLayoutQuestion>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(answer(call.ArgAt<DayLayoutQuestion>(0))));

        return planner;
    }

    private static PersonalTask Owed(string title, DateOnly? dueOn, bool isCompleted = false) => PersonalTask.Restore(
        PersonalTaskId.Create(Guid.CreateVersion7()),
        Person,
        title,
        dueOn,
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        isCompleted);

    private static CalendarEvent Committed(string title, DateTimeOffset start) => CalendarEvent.Create(
        CalendarEventId.Create(Guid.CreateVersion7()),
        CalendarEventTitle.Create(title),
        start,
        start.AddMinutes(30),
        CalendarEventOrigin.Asserted,
        sourceMessage: null,
        importedUid: null,
        recordedAt: start,
        amendedAt: start);

    private static IPersonalTaskStore TaskStoreHolding(IReadOnlyList<PersonalTask> owed)
    {
        var store = Substitute.For<IPersonalTaskStore>();
        store
            .ReadAsync(
                Person,
                PersonalTaskOrigin.Asserted,
                Arg.Any<PersonalTaskCursor?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => owed);

        return store;
    }

    private static TodayLayout Compose(
        IDayLayoutPlanner planner,
        IReadOnlyList<PersonalTask> owed,
        IReadOnlyList<CalendarEvent> committed,
        IPersonalTaskStore? taskStore = null,
        ICalendarEventStore? calendarStore = null)
    {
        var clock = new FakeTimeProvider(DayStart);
        var authorization = AccessAuthorizations.ForUserGranted(Person, MailFathomPermission.MailRead);
        var tasks = taskStore ?? TaskStoreHolding(owed);
        var events = calendarStore ?? Substitute.For<ICalendarEventStore>();

        if (calendarStore is null)
        {
            events
                .ReadRangeAsync(Person, Arg.Any<CalendarEventQuery>(), Arg.Any<CancellationToken>())
                .Returns(_ => committed);
        }

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        return new TodayLayout(
            authorization,
            new OwnTasks(authorization, tasks, clock),
            new OwnCalendar(
                authorization,
                events,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                clock),
            planner,
            SensitiveContentEgressGuards.Inactive());
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the ten routes a person reads and writes their own task list over, the day-layout pair that arranges what it
/// holds included. What is decided here rather than in the use case beneath is the transport half: what a request has
/// to state before it is a task at all, which refusals name a rule rather than echo a value, and that a task this
/// person does not hold is answered as one that does not exist whichever route named it.
/// </summary>
public sealed class ClientTaskEndpointsTests
{
    private static readonly UserId User = SyntheticUser.Deployment;

    private static readonly DateTimeOffset Stamped = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset DayStart = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid TaskIdentifier = new("2f0b1c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid MessageIdentifier = new("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90");

    [Fact]
    public async Task ReadCommittedAsync_APageOfTasks_DescribesEachRowAndTheBoundaryTheNextPageContinuesFrom()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(
                User,
                PersonalTaskOrigin.Asserted,
                null,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => [Kept(dueOn: new DateOnly(2026, 9, 21))]);

        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: 1,
            cursor: null,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result).Value!;
        var row = Assert.Single(page.Tasks);

        Assert.Equal(TaskIdentifier, row.Id);
        Assert.Equal("Answer the tender", row.Title);
        Assert.Equal("2026-09-21", row.DueOn);
        Assert.Equal("Asserted", row.Origin);
        Assert.False(row.Completed);
        Assert.Equal(MessageIdentifier, row.SourceMessageId);
        Assert.NotNull(page.NextCursor);
    }

    /// <summary>The proposals are the other half of the list, and the route is what says which half a page came from.</summary>
    [Fact]
    public async Task ReadProposedAsync_APageOfProposals_ReadsTheProposedOriginRatherThanTheWholeList()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(
                User,
                PersonalTaskOrigin.Proposed,
                null,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => [Kept(dueOn: null, origin: PersonalTaskOrigin.Proposed)]);

        // Act
        var result = await ClientTaskEndpoints.ReadProposedAsync(
            pageSize: null,
            cursor: null,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result).Value!;

        Assert.Equal("Proposed", Assert.Single(page.Tasks).Origin);
        Assert.Null(Assert.Single(page.Tasks).DueOn);
    }

    /// <summary>A boundary this deployment never issued names no page, and the first one would be a list silently jumping back to the top.</summary>
    [Fact]
    public async Task ReadCommittedAsync_ACursorThisDeploymentDidNotIssue_RefusesWithoutEchoingIt()
    {
        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: null,
            cursor: "not-a-cursor-this-deployment-issued",
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("not-a-cursor", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A query string is composed by a page rather than typed, so a screen with nothing to continue from sends an empty value rather than none.</summary>
    [Fact]
    public async Task ReadCommittedAsync_AnEmptyCursor_IsReadAsTheFirstPageRatherThanRefused()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.ReadAsync(User, PersonalTaskOrigin.Asserted, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => []);

        // Act
        var result = await ClientTaskEndpoints.ReadCommittedAsync(
            pageSize: null,
            cursor: "  ",
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientTaskPageResponse>>(result.Result);
    }

    [Fact]
    public async Task FindAsync_ATaskOfTheirOwn_DescribesIt()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21)));

        // Act
        var result = await ClientTaskEndpoints.FindAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TaskIdentifier, Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Id);
    }

    /// <summary>The route constraint admits the all-zero identifier like any other, and no task carries it.</summary>
    [Fact]
    public async Task FindAsync_TheIdentifierNoTaskCarries_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.FindAsync(
            Guid.Empty,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await store.DidNotReceiveWithAnyArgs().FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecordAsync_ATaskAPersonStated_WritesItAndDescribesWhatWasWritten()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest("Answer the tender", "2026-09-21", Reminders: null, DueDayOffsetMinutes: null, MessageIdentifier),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Equal("Answer the tender", written.Title);
        Assert.Equal("2026-09-21", written.DueOn);
        Assert.Equal("Asserted", written.Origin);
        Assert.Equal(MessageIdentifier, written.SourceMessageId);

        await store.Received(1).AddAsync(
            Arg.Is<PersonalTask>(task => task != null && task.User == User && task.Title == "Answer the tender"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A task with no day is one nobody has dated, which is a state rather than a request to refuse.</summary>
    [Fact]
    public async Task RecordAsync_ATaskWithNoDay_WritesItUndated()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest("Answer the tender", DueOn: null, Reminders: null, DueDayOffsetMinutes: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Null(written.DueOn);
        Assert.Null(written.SourceMessageId);
    }

    /// <summary>Every refusal names the rule rather than the value, because a problem document is the part of an answer a proxy log keeps.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("Answer the tender", "21/09/2026")]
    [InlineData("Answer the tender", "2026-9-21")]
    public async Task RecordAsync_ARequestThatStatesNoTask_RefusesWithoutEchoingIt(string? title, string? dueOn)
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(title, dueOn, Reminders: null, DueDayOffsetMinutes: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>The bound is stated rather than thrown, because an overlong title is a client to inform rather than a producer to stop.</summary>
    [Fact]
    public async Task RecordAsync_ATitleLongerThanTheBound_RefusesNamingTheBound()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                new string('x', PersonalTask.MaximumTitleLength + 1),
                DueOn: null,
                Reminders: null,
                DueDayOffsetMinutes: null,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Contains(
            PersonalTask.MaximumTitleLength.ToString(CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A request that carried no document at all is a request to refuse rather than a task with nothing in it.</summary>
    [Fact]
    public async Task RecordAsync_ARequestWithNoBody_Refuses()
    {
        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            request: null,
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReviseAsync_ATaskOfTheirOwn_WritesTheLineAndTheDayAndDescribesTheResult()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21)));
        store.ReviseAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);

        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            TaskIdentifier,
            new ClientTaskRecordRequest("Answer the tender today", "2026-09-22", Reminders: null, DueDayOffsetMinutes: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var revised = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Equal("Answer the tender today", revised.Title);
        Assert.Equal("2026-09-22", revised.DueOn);
        Assert.Equal(MessageIdentifier, revised.SourceMessageId);
    }

    /// <summary>A task this person does not hold is answered as one that does not exist, so nothing reports whose tasks there are.</summary>
    [Fact]
    public async Task ReviseAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns((PersonalTask?)null);

        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            TaskIdentifier,
            new ClientTaskRecordRequest("Answer the tender today", DueOn: null, Reminders: null, DueDayOffsetMinutes: null, SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await store.DidNotReceiveWithAnyArgs().ReviseAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>The identifier is read before the body, so a route naming no task refuses as a missing task rather than as a malformed one.</summary>
    [Fact]
    public async Task ReviseAsync_TheIdentifierNoTaskCarries_AnswersAsOneThatDoesNotExist()
    {
        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            Guid.Empty,
            new ClientTaskRecordRequest("Answer the tender today", DueOn: null, Reminders: null, DueDayOffsetMinutes: null, SourceMessageId: null),
            SignedIn(Substitute.For<IPersonalTaskStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskOfTheirOwn_DescribesTheTaskAsItNowStands()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.SetCompletionAsync(User, PersonalTaskId.Create(TaskIdentifier), true, Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21), isCompleted: true));

        // Act
        var result = await ClientTaskEndpoints.SetCompletionAsync(
            TaskIdentifier,
            new ClientTaskCompletionRequest(Completed: true),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Completed);
    }

    [Fact]
    public async Task SetCompletionAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.SetCompletionAsync(User, PersonalTaskId.Create(TaskIdentifier), true, Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.NotFound);

        // Act
        var result = await ClientTaskEndpoints.SetCompletionAsync(
            TaskIdentifier,
            new ClientTaskCompletionRequest(Completed: true),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task AcceptAsync_AProposalOfTheirOwn_DescribesTheTaskAsItNowStands()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.AcceptAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(dueOn: null));

        // Act
        var result = await ClientTaskEndpoints.AcceptAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Asserted", Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Origin);
    }

    [Fact]
    public async Task AcceptAsync_ATaskThisPersonDoesNotHold_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.AcceptAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.NotFound);

        // Act
        var result = await ClientTaskEndpoints.AcceptAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>Dismissing a proposal is this same erasure, which is why the answer says what happened rather than what was asked for.</summary>
    [Fact]
    public async Task EraseAsync_ATaskOfTheirOwn_ReportsThatItWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.EraseAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Value!.Erased);
        Assert.Equal(TaskIdentifier, result.Value.Id);
    }

    /// <summary>A task already gone and one another person holds are the same answer, and neither is an error.</summary>
    [Fact]
    public async Task EraseAsync_ATaskThisPersonDoesNotHold_ReportsThatNothingWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.EraseAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            TaskIdentifier,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Value!.Erased);
    }

    /// <summary>The identifier no task carries reaches no store, because an erasure of nothing is what a caller that named nothing asked for.</summary>
    [Fact]
    public async Task EraseAsync_TheIdentifierNoTaskCarries_ReportsThatNothingWent()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.EraseAsync(
            Guid.Empty,
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Value!.Erased);
        await store.DidNotReceiveWithAnyArgs().EraseAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A screen asks once whether this deployment arranges a day, and the answer costs no provider call.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrangesDays_ADeploymentInEitherState_SaysWhichWithoutComposingAnything(bool isActive)
    {
        // Arrange
        var planner = Substitute.For<IDayLayoutPlanner>();
        planner.IsActive.Returns(isActive);

        // Act
        var result = ClientTaskEndpoints.ArrangesDays(planner);

        // Assert
        Assert.Equal(isActive, result.Value!.ArrangesDays);
    }

    /// <summary>An arrangement names tasks and windows, and the client draws each row from the task it already holds.</summary>
    [Fact]
    public async Task LayOutTodayAsync_AnArrangementOfTheDay_DescribesEveryPlacementAndWhatDoesNotFit()
    {
        // Arrange
        var deferred = new Guid("3a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");
        var layout = LayingOut(DayLayoutDerivation.Settled(new DayLayoutSuggestion(
            [
                new DayLayoutPlacement(
                    PersonalTaskId.Create(TaskIdentifier),
                    DayStart.AddHours(2),
                    TimeSpan.FromMinutes(45)),
            ],
            [PersonalTaskId.Create(deferred)])));

        // Act
        var result = await ClientTaskEndpoints.LayOutTodayAsync(
            new ClientDayLayoutRequest(DayStart, DayStart.AddHours(12)),
            layout,
            TestContext.Current.CancellationToken);

        // Assert
        var arrangement = Assert.IsType<Ok<ClientDayLayoutResponse>>(result.Result).Value!;
        var placement = Assert.Single(arrangement.Placements);

        Assert.True(arrangement.Arranged);
        Assert.Equal(TaskIdentifier, placement.TaskId);
        Assert.Equal(DayStart.AddHours(2), placement.StartAt);
        Assert.Equal(45, placement.Minutes);
        Assert.Equal(deferred, Assert.Single(arrangement.NotToday));
    }

    /// <summary>A deployment that arranges no day and a provider that did not answer are one answer to a screen.</summary>
    [Theory]
    [InlineData(DayLayoutWithholding.NotActivated)]
    [InlineData(DayLayoutWithholding.ProviderUnavailable)]
    public async Task LayOutTodayAsync_AnArrangementWithheld_SaysTheDayWasNotArranged(DayLayoutWithholding withholding)
    {
        // Arrange
        var layout = LayingOut(DayLayoutDerivation.Withholding(withholding));

        // Act
        var result = await ClientTaskEndpoints.LayOutTodayAsync(
            new ClientDayLayoutRequest(DayStart, DayStart.AddHours(12)),
            layout,
            TestContext.Current.CancellationToken);

        // Assert
        var arrangement = Assert.IsType<Ok<ClientDayLayoutResponse>>(result.Result).Value!;

        Assert.False(arrangement.Arranged);
        Assert.Empty(arrangement.Placements);
        Assert.Empty(arrangement.NotToday);
    }

    /// <summary>A spent allowance travels, because pressing the control again is what a person would otherwise do.</summary>
    [Fact]
    public async Task LayOutTodayAsync_TheDeploymentsAllowanceSpent_ReportsThatRatherThanAnEmptyDay()
    {
        // Arrange
        var layout = LayingOut(DayLayoutDerivation.Withholding(DayLayoutWithholding.AllowanceExhausted));

        // Act
        var result = await ClientTaskEndpoints.LayOutTodayAsync(
            new ClientDayLayoutRequest(DayStart, DayStart.AddHours(12)),
            layout,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status429TooManyRequests, refusal.StatusCode);
    }

    /// <summary>Which hours are somebody's day is their client's to state, so a request stating none is not a day.</summary>
    [Fact]
    public async Task LayOutTodayAsync_ARequestStatingNoWindow_RefusesItWithoutArrangingAnything()
    {
        // Arrange
        var layout = LayingOut(DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));

        // Act
        var result = await ClientTaskEndpoints.LayOutTodayAsync(
            new ClientDayLayoutRequest(DayStart, Until: null),
            layout,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>A window the reading does not answer for is reported as a day this deployment does not arrange.</summary>
    [Fact]
    public async Task LayOutTodayAsync_AWindowTheReadingRefuses_NamesTheRuleRatherThanTheValue()
    {
        // Arrange
        var layout = LayingOut(DayLayoutDerivation.Settled(new DayLayoutSuggestion([], [])));

        // Act
        var result = await ClientTaskEndpoints.LayOutTodayAsync(
            new ClientDayLayoutRequest(DayStart, DayStart.AddDays(30)),
            layout,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>Composes the reading over a planner answering what a case states, and a list holding one task due that day.</summary>
    private static TodayLayout LayingOut(DayLayoutDerivation derivation)
    {
        var authorization = AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailRead);
        var store = Substitute.For<IPersonalTaskStore>();
        store
            .ReadAsync(
                User,
                PersonalTaskOrigin.Asserted,
                Arg.Any<PersonalTaskCursor?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => [Kept(dueOn: DateOnly.FromDateTime(DayStart.DateTime))]);

        var calendar = Substitute.For<ICalendarEventStore>();
        calendar
            .ReadRangeAsync(User, Arg.Any<CalendarEventQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => []);

        var planner = Substitute.For<IDayLayoutPlanner>();
        planner.IsActive.Returns(true);
        planner
            .SuggestAsync(Arg.Any<DayLayoutQuestion>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(derivation));

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Substitute.For<IPersistenceSession>());

        var clock = new FakeTimeProvider(Stamped);

        return new TodayLayout(
            authorization,
            new OwnTasks(authorization, store, clock),
            new OwnCalendar(
                authorization,
                calendar,
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                clock),
            planner,
            SensitiveContentEgressGuards.Inactive());
    }

    private static OwnTasks SignedIn(IPersonalTaskStore store) => new(
        AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailRead),
        store,
        new FakeTimeProvider(Stamped));

    /// <summary>
    /// The presets the design draws for a due date, written whole and answered with the instants they fall at — which
    /// travel because the hour a due day is measured back from is this deployment's rule rather than a client's.
    /// </summary>
    [Fact]
    public async Task RecordAsync_ATaskAnnouncedAtTheDueDatePresets_WritesThemAndSaysWhenEachFalls()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                "Answer the tender",
                "2026-09-21",
                Reminders: [0, 60, 240, 1440, 2880],
                DueDayOffsetMinutes: 120,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Equal([2880, 1440, 240, 60, 0], written.Reminders);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.FromHours(2)),
            written.RemindsAt[^1]);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 19, 9, 0, 0, TimeSpan.FromHours(2)),
            written.RemindsAt[0]);
    }

    /// <summary>A task nobody dated announces nothing, whatever a client states, because a lead has nothing to measure from.</summary>
    [Fact]
    public async Task RecordAsync_RemindersOnATaskWithNoDay_RefusesNamingTheRule()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                "Answer the tender",
                DueOn: null,
                Reminders: [60],
                DueDayOffsetMinutes: 120,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every rule a set of leads is held to is stated here rather than raised out of the record, because a lead
    /// somebody typed is a client to inform rather than a producer to stop.
    /// </summary>
    [Theory]
    [InlineData(new[] { -1 }, 120)]
    [InlineData(new[] { Reminder.MaximumMinutesBefore + 1 }, 120)]
    [InlineData(new[] { 60, 60 }, 120)]
    [InlineData(new[] { 60 }, null)]
    [InlineData(new[] { 60 }, 15 * 60)]
    [InlineData(new[] { 60 }, -13 * 60)]
    public async Task RecordAsync_ASetOfLeadsNoTaskMayCarry_RefusesWithoutEchoingIt(
        int[] reminders,
        int? dueDayOffsetMinutes)
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                "Answer the tender",
                "2026-09-21",
                reminders,
                dueDayOffsetMinutes,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        await store.DidNotReceiveWithAnyArgs().AddAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>More leads than one task may carry is the bound stated rather than the record raising for it.</summary>
    [Fact]
    public async Task RecordAsync_MoreLeadsThanOneTaskMayCarry_RefusesNamingTheBound()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                "Answer the tender",
                "2026-09-21",
                [.. Enumerable.Range(1, Reminder.MaximumCount + 1)],
                DueDayOffsetMinutes: 120,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            Reminder.MaximumCount.ToString(CultureInfo.InvariantCulture),
            Assert.IsType<ProblemHttpResult>(result.Result).ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>Stating no lead is a task that announces nothing rather than a request to refuse, whatever offset it carries.</summary>
    [Fact]
    public async Task RecordAsync_ATaskStatingNoLead_AnnouncesNothing()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();

        // Act
        var result = await ClientTaskEndpoints.RecordAsync(
            new ClientTaskRecordRequest(
                "Answer the tender",
                "2026-09-21",
                Reminders: [],
                DueDayOffsetMinutes: 120,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!;

        Assert.Empty(written.Reminders);
        Assert.Empty(written.RemindsAt);
    }

    /// <summary>
    /// Turning the last reminder off is an edit stating a task with none, which is what makes the reminders part of
    /// the record an edit states rather than a field it may leave out.
    /// </summary>
    [Fact]
    public async Task ReviseAsync_ARevisionStatingNoLead_WritesATaskThatAnnouncesNothing()
    {
        // Arrange
        var store = Substitute.For<IPersonalTaskStore>();
        store.FindAsync(User, PersonalTaskId.Create(TaskIdentifier), Arg.Any<CancellationToken>())
            .Returns(Kept(new DateOnly(2026, 9, 21), reminders: [60]));
        store.ReviseAsync(Arg.Any<PersonalTask>(), Arg.Any<CancellationToken>())
            .Returns(PersonalTaskChangeOutcome.Applied);

        // Act
        var result = await ClientTaskEndpoints.ReviseAsync(
            TaskIdentifier,
            new ClientTaskRecordRequest(
                "Answer the tender",
                "2026-09-21",
                Reminders: null,
                DueDayOffsetMinutes: 120,
                SourceMessageId: null),
            SignedIn(store),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.IsType<Ok<ClientTaskResponse>>(result.Result).Value!.Reminders);
    }

    private static PersonalTask Kept(
        DateOnly? dueOn,
        PersonalTaskOrigin origin = PersonalTaskOrigin.Asserted,
        bool isCompleted = false,
        IReadOnlyCollection<int>? reminders = null) =>
        PersonalTask.Restore(
            PersonalTaskId.Create(TaskIdentifier),
            User,
            "Answer the tender",
            dueOn,
            reminders is null or { Count: 0 }
                ? TaskAnnouncement.Silent
                : new TaskAnnouncement(TimeSpan.FromHours(2), [.. reminders.Select(Reminder.Create)]),
            origin,
            StoredEmailId.Create(MessageIdentifier),
            isCompleted);
}

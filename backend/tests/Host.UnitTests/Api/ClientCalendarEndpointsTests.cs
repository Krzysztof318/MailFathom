// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Reminders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the signed-in person's calendar routes decide about a request, and what an answer of theirs may carry.</summary>
/// <remarks>
/// <para>
/// The calendar's own rules and the grant each act asks for are <c>OwnCalendar</c>'s, covered there and not repeated
/// here. What these routes decide is the part above them: which request is a request at all, which half of the
/// calendar a window reads, how each refusal reaches a client as a status it can act on, and where the honest answer
/// is that the calendar holds no such event rather than that the request was wrong.
/// </para>
/// <para>
/// Every refusal is asserted for what it does <em>not</em> carry as much as for what it says. A title says who
/// somebody is meeting and the times say when they are not somewhere else, and a problem document is the one part of
/// an answer that a proxy log, a trace, and a client's captured error all keep.
/// </para>
/// </remarks>
public sealed class ClientCalendarEndpointsTests
{
    private static readonly MailUserId Person = SyntheticMailUser.Deployment;
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly ICalendarEventStore store = Substitute.For<ICalendarEventStore>();
    private readonly FakeTimeProvider clock = new(Now);

    /// <summary>
    /// The deployment's half of an agreement with a client it cannot reference. The client composes these paths from
    /// constants of its own, and a rename on either side compiles cleanly while every calendar screen reaches a 404.
    /// </summary>
    [Fact]
    public void CalendarRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/calendar", ClientCalendarEndpoints.CalendarRoute);
        Assert.Equal("/calendar/{eventId:guid}", ClientCalendarEndpoints.CalendarEventRoute);
        Assert.Equal("/calendar/{eventId:guid}/acceptance", ClientCalendarEndpoints.CalendarEventAcceptanceRoute);
    }

    [Fact]
    public async Task ReadWindowAsync_AWindowOfTheirCalendar_AnswersTheEventsInIt()
    {
        // Arrange
        this.AnswersWindowWith(Proposal(Monday, "Interview"));

        // Act
        var result = await ClientCalendarEndpoints.ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            count: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.Single(Assert.IsType<Ok<CalendarWindowResponse>>(result.Result).Value!.Events);
        Assert.Equal("Interview", answered.Title);
        Assert.Equal(Monday, answered.Start);
    }

    /// <summary>The spelling a client branches on is the name, which is what it also sends back when it narrows a window.</summary>
    [Theory]
    [InlineData(CalendarEventOrigin.Asserted, "Asserted")]
    [InlineData(CalendarEventOrigin.Proposed, "Proposed")]
    public async Task ReadWindowAsync_AnEventOfEitherHalf_NamesItsOriginByItsPublishedSpelling(
        CalendarEventOrigin origin,
        string published)
    {
        // Arrange
        this.AnswersWindowWith(Compose(Monday, "Interview", origin));

        // Act
        var result = await ClientCalendarEndpoints.ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            count: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            published,
            Assert.Single(Assert.IsType<Ok<CalendarWindowResponse>>(result.Result).Value!.Events).Origin);
    }

    /// <summary>A half named by the number behind it would rely on an ordering this repository is free to change.</summary>
    [Theory]
    [InlineData("proposed")]
    [InlineData("Asserted")]
    public async Task ReadWindowAsync_AHalfNamedByItsSpelling_NarrowsTheWindowToIt(string stated)
    {
        // Arrange
        this.AnswersWindowWith();

        // Act
        await ClientCalendarEndpoints.ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            stated,
            count: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Enum.Parse<CalendarEventOrigin>(stated, ignoreCase: true), this.WindowRead().Origin);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("everything")]
    public async Task ReadWindowAsync_AHalfThatNamesNeither_IsRefused(string stated)
    {
        // Act
        var result = await ClientCalendarEndpoints.ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            stated,
            count: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReadWindowAsync_ARequestStatingNoSpan_IsRefusedRatherThanReadingEverything()
    {
        // Act
        var result = await ClientCalendarEndpoints.ReadWindowAsync(
            from: null,
            until: null,
            origin: null,
            count: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task ReadWindowAsync_ACountAboveWhatThisDeploymentAnswers_IsRefusedNamingTheBound()
    {
        // Act
        var result = await ClientCalendarEndpoints.ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            CalendarEventQuery.MaximumCount + 1,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(
            CalendarEventQuery.MaximumCount.ToString(CultureInfo.InvariantCulture),
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>A UUID route constraint admits the all-zero value, which names no event and must not reach a guard as a fault.</summary>
    [Fact]
    public async Task FindAsync_TheOneIdentifierNoEventCarries_AnswersAsOneThatDoesNotExist()
    {
        // Act
        var result = await ClientCalendarEndpoints.FindAsync(
            Guid.Empty,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task FindAsync_AnEventThatCalendarDoesNotHold_AnswersNotFound()
    {
        // Arrange
        this.store
            .ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CalendarEventId>(), Arg.Any<CancellationToken>())
            .Returns((CalendarEvent?)null);

        // Act
        var result = await ClientCalendarEndpoints.FindAsync(
            Guid.CreateVersion7(Now),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task CreateAsync_ARequestWithNoBody_IsRefused()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            request: null,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task CreateAsync_AnEventStatingNoStart_IsRefused()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Standup", Start: null, End: null, IsAllDay: false, Reminders: null, SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task CreateAsync_AMessageIdentifierThatNamesNoMessage_IsRefused()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Standup", Monday, End: null, IsAllDay: false, Reminders: null, Guid.Empty),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>The regression this exists for: a problem document carrying the title somebody typed into a proxy log.</summary>
    [Fact]
    public async Task CreateAsync_ATitleThatIsNotOne_IsRefusedWithoutEchoingIt()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Lunch‮with Anna", Monday, End: null, IsAllDay: false, Reminders: null, SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("Anna", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_AnEventSomebodyStated_AnswersItAsTheCalendarNowHoldsIt()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Standup", Monday, Monday.AddMinutes(30), IsAllDay: false, Reminders: null, SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!;
        Assert.Equal("Standup", written.Title);
        Assert.Equal(nameof(CalendarEventOrigin.Asserted), written.Origin);
        Assert.Equal(Now, written.RecordedAt);
    }

    [Fact]
    public async Task AmendAsync_AnEventThatCalendarDoesNotHold_AnswersNotFound()
    {
        // Arrange
        this.store
            .ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CalendarEventId>(), Arg.Any<CancellationToken>())
            .Returns((CalendarEvent?)null);

        // Act
        var result = await ClientCalendarEndpoints.AmendAsync(
            Guid.CreateVersion7(Now),
            new CalendarEventAmendmentRequest("Standup", Monday, End: null, IsAllDay: false, Reminders: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task AmendAsync_TheRecordTheEventIsToStandAs_AnswersTheAmendedEvent()
    {
        // Arrange
        var held = Proposal(Monday, "Interview");
        this.Holds(held);

        // Act
        var result = await ClientCalendarEndpoints.AmendAsync(
            held.Id.Value,
            new CalendarEventAmendmentRequest("Interview, moved", Monday.AddHours(2), End: null, IsAllDay: false, Reminders: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var amended = Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!;
        Assert.Equal("Interview, moved", amended.Title);
        Assert.Equal(Monday.AddHours(2), amended.Start);
        Assert.Equal(nameof(CalendarEventOrigin.Proposed), amended.Origin);
    }

    [Fact]
    public async Task AcceptAsync_ADateTheirMailProposed_AnswersItOnTheCalendar()
    {
        // Arrange
        var held = Proposal(Monday, "Interview");
        this.Holds(held);

        // Act
        var result = await ClientCalendarEndpoints.AcceptAsync(
            held.Id.Value,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            nameof(CalendarEventOrigin.Asserted),
            Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!.Origin);
    }

    /// <summary>A second acceptance is a conflict rather than a repeat, so a client is told the act was somebody else's.</summary>
    [Fact]
    public async Task AcceptAsync_AnEventAlreadyOnTheCalendar_AnswersAConflict()
    {
        // Arrange
        var held = Compose(Monday, "Standup", CalendarEventOrigin.Asserted);
        this.Holds(held);

        // Act
        var result = await ClientCalendarEndpoints.AcceptAsync(
            held.Id.Value,
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task DeleteAsync_AnEventTheCalendarHeld_AnswersThatItIsGone()
    {
        // Arrange
        this.store
            .DeleteAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailUserId>(),
                Arg.Any<CalendarEventId>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await ClientCalendarEndpoints.DeleteAsync(
            Guid.CreateVersion7(Now),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task DeleteAsync_AnEventThatCalendarDoesNotHold_AnswersNotFound()
    {
        // Arrange
        this.store
            .DeleteAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailUserId>(),
                Arg.Any<CalendarEventId>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await ClientCalendarEndpoints.DeleteAsync(
            Guid.CreateVersion7(Now),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A client reads the leads back and the instants they fall at, so it never derives the second itself.</summary>
    [Fact]
    public async Task CreateAsync_AnEventStatedWithReminders_AnswersTheLeadsAndWhenEachComesDue()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Standup", Monday, End: null, IsAllDay: false, [15, 1440], SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!;
        Assert.Equal([1440, 15], written.Reminders);
        Assert.Equal([Monday.AddDays(-1), Monday.AddMinutes(-15)], written.RemindsAt);
    }

    /// <summary>An event stating nothing about reminders announces nothing rather than being refused for saying nothing.</summary>
    [Fact]
    public async Task CreateAsync_ARequestNamingNoReminders_WritesAnEventThatAnnouncesNothing()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Standup", Monday, End: null, IsAllDay: false, Reminders: null, SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!.Reminders);
    }

    /// <summary>A lead nobody can set is reported as a lead rather than as a fault in the deployment.</summary>
    [Fact]
    public async Task CreateAsync_ALeadNoReminderMayState_IsRefused()
    {
        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest(
                "Standup",
                Monday,
                End: null,
                IsAllDay: false,
                [Reminder.MaximumMinutesBefore + 1],
                SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A day is announced from the morning of it, and the client reads that instant rather than deriving it.</summary>
    [Fact]
    public async Task CreateAsync_ADayRatherThanAClockTime_AnswersTheInstantMeasuredFromThatMorning()
    {
        // Arrange
        var theDay = new DateTimeOffset(Monday.Date, TimeSpan.Zero);

        // Act
        var result = await ClientCalendarEndpoints.CreateAsync(
            new CalendarEventCreationRequest("Anna's birthday", theDay, End: null, IsAllDay: true, [0], SourceMessage: null),
            this.Calendar(),
            TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.IsType<Ok<CalendarEventResponse>>(result.Result).Value!;
        Assert.True(written.IsAllDay);
        Assert.Equal([theDay.AddHours(CalendarEvent.AllDayReminderHour)], written.RemindsAt);
    }

    private static CalendarEvent Proposal(DateTimeOffset start, string title) =>
        Compose(start, title, CalendarEventOrigin.Proposed);

    private static CalendarEvent Compose(DateTimeOffset start, string title, CalendarEventOrigin origin) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7(start)),
            CalendarEventTitle.Create(title),
            start,
            end: null,
            isAllDay: false,
            reminders: [],
            origin,
            sourceMessage: null,
            importedUid: null,
            Now,
            Now);

    private void Holds(CalendarEvent calendarEvent)
    {
        this.store
            .ReadAsync(Arg.Any<MailUserId>(), Arg.Any<CalendarEventId>(), Arg.Any<CancellationToken>())
            .Returns(calendarEvent);
        this.store
            .ReplaceAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailUserId>(),
                Arg.Any<CalendarEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private void AnswersWindowWith(params CalendarEvent[] events) =>
        this.store
            .ReadRangeAsync(Arg.Any<MailUserId>(), Arg.Any<CalendarEventQuery>(), Arg.Any<CancellationToken>())
            .Returns(events);

    /// <summary>Reads the window the route composed, which is what each narrowing is asserted against.</summary>
    private CalendarEventQuery WindowRead() =>
        (CalendarEventQuery)this.store
            .ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(ICalendarEventStore.ReadRangeAsync))
            .GetArguments()[1]!;

    private OwnCalendar Calendar()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OwnCalendar(
            AccessAuthorizations.ForUserGranted(Person, MailFathomPermission.MailRead),
            this.store,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
            this.clock);
    }

    /// <summary>A session that commits whatever was staged in it, which is what a write's ordinary path needs.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar;

/// <summary>
/// Covers the use case a person reads and writes their own calendar through. What it has to hold is that the calendar
/// acted on is the one the credential authenticated rather than one a caller could name, that a window outside what
/// this deployment answers is refused rather than narrowed, that what a person types reaches the calendar asserted,
/// that an amendment keeps everything an amendment keeps, and that accepting a proposal happens once.
/// </summary>
public sealed class OwnCalendarTests
{
    private static readonly MailUserId Person = SyntheticMailUser.Deployment;
    private static readonly MailUserId SomebodyElse = SyntheticMailUser.Another;
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCalendarEventStore store = new();
    private readonly FakeTimeProvider clock = new(Now);

    [Fact]
    public async Task ReadWindowAsync_ACalendarWithEventsInIt_AnswersTheirOwnEarliestFirst()
    {
        // Arrange
        this.store.Hold(Person, EventAt(Monday.AddHours(3), "later"));
        this.store.Hold(Person, EventAt(Monday, "earlier"));
        this.store.Hold(SomebodyElse, EventAt(Monday.AddHours(1), "somebody else's"));

        // Act
        var window = await this.SignedIn().ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            count: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["earlier", "later"], window!.Select(calendarEvent => calendarEvent.Title.Value));
    }

    /// <summary>The proposals are drawn somewhere other than the calendar, so the half is asked for rather than sorted out afterwards.</summary>
    [Fact]
    public async Task ReadWindowAsync_NarrowedToWhatMailProposed_AnswersThatHalfAlone()
    {
        // Arrange
        this.store.Hold(Person, EventAt(Monday, "typed"));
        this.store.Hold(Person, Proposal(Monday.AddHours(1), "proposed"));

        // Act
        var window = await this.SignedIn().ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            CalendarEventOrigin.Proposed,
            count: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("proposed", Assert.Single(window!).Title.Value);
    }

    /// <summary>A screen that asked for a year and was served a month would be missing eleven of them while believing it drew the year.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CalendarEventQuery.MaximumCount + 1)]
    public async Task ReadWindowAsync_ACountOutsideWhatThisDeploymentAnswers_IsRefusedRatherThanNarrowed(int count)
    {
        // Act
        var window = await this.SignedIn().ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            count,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(window);
    }

    [Fact]
    public async Task ReadWindowAsync_AWindowThatClosesBeforeItOpens_IsRefused()
    {
        // Act
        var window = await this.SignedIn().ReadWindowAsync(
            Monday,
            Monday,
            origin: null,
            count: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(window);
    }

    [Fact]
    public async Task ReadWindowAsync_ARequestNamingNoCount_ReadsUnderTheDefaultBound()
    {
        // Act
        await this.SignedIn().ReadWindowAsync(
            Monday,
            Monday.AddDays(1),
            origin: null,
            count: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventQuery.DefaultCount, this.store.LastQuery!.Count);
    }

    /// <summary>The regression this exists for: an identifier learned elsewhere reaching somebody else's day.</summary>
    [Fact]
    public async Task FindAsync_AnEventOfAnotherPersonsCalendar_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var theirs = EventAt(Monday, "theirs");
        this.store.Hold(SomebodyElse, theirs);

        // Act
        var found = await this.SignedIn().FindAsync(theirs.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public async Task CreateAsync_AnEventSomebodyTyped_ReachesTheirCalendarAsserted()
    {
        // Act
        var written = await this.SignedIn().CreateAsync(
            "Standup",
            Monday,
            Monday.AddMinutes(30),
            sourceMessage: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.Written, written.Outcome);
        Assert.Equal(CalendarEventOrigin.Asserted, written.Event!.Origin);
        Assert.Equal(Now, written.Event.RecordedAt);
        Assert.Equal(Now, written.Event.AmendedAt);
        Assert.Equal(written.Event, await this.SignedIn().FindAsync(written.Event.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>An event created from an open thread keeps the message, so the conversation can be opened from the day.</summary>
    [Fact]
    public async Task CreateAsync_AnEventCreatedFromAnOpenThread_CitesTheMessage()
    {
        // Arrange
        var message = StoredEmailId.Create(new Guid("4a3f2c1d-0e9b-4a8c-9d7e-6f5a4b3c2d1e"));

        // Act
        var written = await this.SignedIn().CreateAsync(
            "Kick-off",
            Monday,
            end: null,
            message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(message, written.Event!.SourceMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Lunch‮with Anna")]
    public async Task CreateAsync_ATitleThatIsNotOne_IsRefusedWithNothingWritten(string? title)
    {
        // Act
        var written = await this.SignedIn().CreateAsync(
            title,
            Monday,
            end: null,
            sourceMessage: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.TitleRefused, written.Outcome);
        Assert.Null(written.Event);
    }

    /// <summary>An entry whose span is empty or reversed is a reading that went wrong rather than something to draw.</summary>
    [Fact]
    public async Task CreateAsync_AnEndThatIsNotAfterTheStart_IsRefused()
    {
        // Act
        var written = await this.SignedIn().CreateAsync(
            "Review",
            Monday,
            Monday,
            sourceMessage: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.EndNotAfterStart, written.Outcome);
    }

    [Fact]
    public async Task AmendAsync_AnEventOfTheirOwn_KeepsEverythingAnAmendmentKeeps()
    {
        // Arrange
        var message = StoredEmailId.Create(new Guid("5b4e3d2c-1f0a-4b9d-8c7e-6d5f4a3b2c1d"));
        var held = CalendarEvent.Create(
            CalendarEventId.Create(new Guid("7c6d5e4f-3a2b-4c1d-9e8f-7a6b5c4d3e2f")),
            CalendarEventTitle.Create("Review"),
            Monday,
            Monday.AddHours(1),
            CalendarEventOrigin.Proposed,
            message,
            importedUid: null,
            Now,
            Now);

        this.store.Hold(Person, held);
        this.clock.Advance(TimeSpan.FromHours(2));

        // Act
        var written = await this.SignedIn().AmendAsync(
            held.Id,
            "Review, moved",
            Monday.AddHours(3),
            end: null,
            TestContext.Current.CancellationToken);

        // Assert
        var amended = written.Event!;
        Assert.Equal(CalendarEventWriteOutcome.Written, written.Outcome);
        Assert.Equal("Review, moved", amended.Title.Value);
        Assert.Equal(Monday.AddHours(3), amended.Start);
        Assert.Null(amended.End);
        Assert.Equal(held.Id, amended.Id);
        Assert.Equal(CalendarEventOrigin.Proposed, amended.Origin);
        Assert.Equal(message, amended.SourceMessage);
        Assert.Equal(Now, amended.RecordedAt);
        Assert.Equal(this.clock.GetUtcNow(), amended.AmendedAt);
    }

    [Fact]
    public async Task AmendAsync_AnEventOfAnotherPersonsCalendar_WritesNothingAndAnswersAsNotFound()
    {
        // Arrange
        var theirs = EventAt(Monday, "theirs");
        this.store.Hold(SomebodyElse, theirs);

        // Act
        var written = await this.SignedIn().AmendAsync(
            theirs.Id,
            "mine now",
            Monday,
            end: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.NotFound, written.Outcome);
        Assert.Equal("theirs", (await this.store.ReadAsync(SomebodyElse, theirs.Id, TestContext.Current.CancellationToken))!.Title.Value);
    }

    /// <summary>A refused record never reaches the calendar, so the event stands exactly as it did.</summary>
    [Fact]
    public async Task AmendAsync_ATitleThatIsNotOne_IsRefusedAndLeavesTheEventStanding()
    {
        // Arrange
        var held = EventAt(Monday, "Review");
        this.store.Hold(Person, held);

        // Act
        var written = await this.SignedIn().AmendAsync(
            held.Id,
            "  ",
            Monday,
            end: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.TitleRefused, written.Outcome);
        Assert.Equal("Review", (await this.SignedIn().FindAsync(held.Id, TestContext.Current.CancellationToken))!.Title.Value);
    }

    [Fact]
    public async Task AcceptAsync_ADateTheirMailProposed_PutsItOnTheCalendarUnderTheSameIdentity()
    {
        // Arrange
        var proposed = Proposal(Monday, "Interview");
        this.store.Hold(Person, proposed);
        this.clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var written = await this.SignedIn().AcceptAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.Written, written.Outcome);
        Assert.Equal(proposed.Id, written.Event!.Id);
        Assert.Equal(CalendarEventOrigin.Asserted, written.Event.Origin);
        Assert.Equal(this.clock.GetUtcNow(), written.Event.AmendedAt);
    }

    /// <summary>The second acceptance is a caller acting on a proposal somebody already took, and reporting it as done would move the record of when.</summary>
    [Fact]
    public async Task AcceptAsync_AnEventAlreadyOnTheCalendar_IsRefusedRatherThanRepeated()
    {
        // Arrange
        var held = EventAt(Monday, "Standup");
        this.store.Hold(Person, held);

        // Act
        var written = await this.SignedIn().AcceptAsync(held.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.AlreadyOnTheCalendar, written.Outcome);
    }

    [Fact]
    public async Task AcceptAsync_AnEventOfAnotherPersonsCalendar_AnswersAsNotFound()
    {
        // Arrange
        var theirs = Proposal(Monday, "theirs");
        this.store.Hold(SomebodyElse, theirs);

        // Act
        var written = await this.SignedIn().AcceptAsync(theirs.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarEventWriteOutcome.NotFound, written.Outcome);
    }

    /// <summary>Dismissing a date nobody wanted is this same act, which is why no route of its own exists for it.</summary>
    [Fact]
    public async Task DeleteAsync_AProposalNobodyWanted_TakesTheRowOut()
    {
        // Arrange
        var proposed = Proposal(Monday, "Interview");
        this.store.Hold(Person, proposed);

        // Act
        var deleted = await this.SignedIn().DeleteAsync(proposed.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(deleted);
        Assert.Null(await this.SignedIn().FindAsync(proposed.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_AnEventOfAnotherPersonsCalendar_RemovesNothingAndAnswersSo()
    {
        // Arrange
        var theirs = EventAt(Monday, "theirs");
        this.store.Hold(SomebodyElse, theirs);

        // Act
        var deleted = await this.SignedIn().DeleteAsync(theirs.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(deleted);
        Assert.NotNull(await this.store.ReadAsync(SomebodyElse, theirs.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>A grant that answers from mail is not a grant that reads it, so it reaches none of the calendar either.</summary>
    [Fact]
    public async Task ReadWindowAsync_ACallerWithoutTheMailReadingGrant_IsRefused()
    {
        // Arrange
        var calendar = this.CalendarFor(MailFathomPermission.MailAsk);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => calendar.ReadWindowAsync(
                Monday,
                Monday.AddDays(1),
                origin: null,
                count: null,
                TestContext.Current.CancellationToken));
    }

    /// <summary>Every write here is asked for under the same grant the reads are, so a caller without it changes nothing.</summary>
    [Fact]
    public async Task CreateAsync_ACallerWithoutTheMailReadingGrant_IsRefused()
    {
        // Arrange
        var calendar = this.CalendarFor(MailFathomPermission.MailAsk);

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => calendar.CreateAsync(
                "Standup",
                Monday,
                end: null,
                sourceMessage: null,
                TestContext.Current.CancellationToken));
    }

    private static CalendarEvent EventAt(DateTimeOffset start, string title) =>
        Compose(start, title, CalendarEventOrigin.Asserted);

    private static CalendarEvent Proposal(DateTimeOffset start, string title) =>
        Compose(start, title, CalendarEventOrigin.Proposed);

    private static CalendarEvent Compose(DateTimeOffset start, string title, CalendarEventOrigin origin) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7(start)),
            CalendarEventTitle.Create(title),
            start,
            end: null,
            origin,
            sourceMessage: null,
            importedUid: null,
            Now,
            Now);

    private OwnCalendar SignedIn() => this.CalendarFor(MailFathomPermission.MailRead);

    private OwnCalendar CalendarFor(params MailFathomPermission[] granted)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OwnCalendar(
            AccessAuthorizations.ForUserGranted(Person, granted),
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

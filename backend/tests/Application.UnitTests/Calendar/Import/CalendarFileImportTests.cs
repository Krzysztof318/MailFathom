// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.Calendar.Import;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar.Import;

/// <summary>
/// Covers the use case an offered iCalendar file reaches somebody's calendar through. What it has to hold is that the
/// summary writes nothing while reporting exactly what the import would write, that an entry this calendar already
/// holds is counted rather than written a second time, that a file refused as a whole leaves the calendar untouched,
/// that what is written is asserted and keeps the identifier it was imported under, and that the calendar reached is
/// the one the credential authenticated.
/// </summary>
public sealed class CalendarFileImportTests
{
    private static readonly UserId Person = SyntheticUser.Deployment;
    private static readonly UserId SomebodyElse = SyntheticUser.Another;
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCalendarEventStore store = new();
    private readonly FakeTimeProvider clock = new(Now);
    private readonly ICalendarFileReader reader = Substitute.For<ICalendarFileReader>();
    private readonly Queue<PersistenceCommitResult> commits = new();
    private UserTimeZone zone = UserTimeZone.Coordinated;

    [Fact]
    public async Task SummariseAsync_AFileWithEntriesInIt_ReportsWhatItWouldCreateAndWritesNothing()
    {
        // Arrange
        this.Reads(Entry("one", Monday), Entry("two", Monday.AddDays(2)));

        // Act
        var summary = await this.SignedIn().SummariseAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CalendarImportOutcome.Read, summary.Outcome);
        Assert.Equal(2, summary.Events);
        Assert.Equal(Monday, summary.Earliest);
        Assert.Equal(Monday.AddDays(2), summary.Latest);
        Assert.Empty(await this.HeldAsync());
    }

    [Fact]
    public async Task ImportAsync_AFileWithEntriesInIt_PutsThemOnTheCalendarAsserted()
    {
        // Arrange
        this.Reads(Entry("one", Monday));

        // Act
        var summary = await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        var written = Assert.Single(await this.HeldAsync());

        Assert.Equal(1, summary.Events);
        Assert.Equal(CalendarEventOrigin.Asserted, written.Origin);
        Assert.Equal("one", written.ImportedUid?.Value);
        Assert.Equal(Monday, written.Start);
        Assert.Equal(Now, written.RecordedAt);
        Assert.Empty(written.Reminders);
        Assert.Null(written.SourceMessage);
    }

    /// <summary>Importing the same file twice is the mistake this whole identifier exists to make cost a message rather than a doubled calendar.</summary>
    [Fact]
    public async Task ImportAsync_TheSameFileASecondTime_CreatesNothingAndCountsEveryEntryAsAlreadyHeld()
    {
        // Arrange
        this.Reads(Entry("one", Monday), Entry("two", Monday.AddHours(2)));
        await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Act
        var again = await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, again.Events);
        Assert.Equal(2, await this.HeldCountAsync());
        Assert.Equal(
            new CalendarImportSkipTally(CalendarImportSkipReason.AlreadyOnTheCalendar, 2),
            Assert.Single(again.Skipped));
    }

    /// <summary>
    /// The race the imported identifier's unique index refuses: two imports of one file both read a calendar holding
    /// neither entry, so the loser has to decide again from what the winner committed rather than replay the rows its
    /// first read composed — which would violate that index on every attempt until they ran out, and reach the person
    /// as a failed request instead of the count the summary promises.
    /// </summary>
    [Fact]
    public async Task ImportAsync_AnAttemptWhoseCommitLostTheRace_ReadsWhatTheCalendarHoldsAgainRatherThanReplayingIt()
    {
        // Arrange
        this.commits.Enqueue(PersistenceCommitResult.ConcurrencyConflict);
        this.Reads(Entry("one", Monday), Entry("two", Monday.AddHours(2)));

        // Act
        var summary = await this.AdvancePastTheBackoffAsync(
            this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(CalendarImportOutcome.Read, summary.Outcome);
        Assert.Equal(2, this.store.ImportedUidReads);
    }

    /// <summary>Somebody else having imported the file says nothing about this person's calendar.</summary>
    [Fact]
    public async Task ImportAsync_AnIdentifierOnlySomebodyElsesCalendarHolds_WritesTheEventAnyway()
    {
        // Arrange
        this.store.Hold(SomebodyElse, Imported("one"));
        this.Reads(Entry("one", Monday));

        // Act
        var summary = await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, summary.Events);
        Assert.Single(await this.HeldAsync());
    }

    [Fact]
    public async Task ImportAsync_AFileNamingOneIdentifierTwice_WritesItOnceAndCountsTheRepeat()
    {
        // Arrange
        this.Reads(Entry("one", Monday), Entry("one", Monday.AddHours(3)));

        // Act
        var summary = await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, summary.Events);
        Assert.Equal(Monday, Assert.Single(await this.HeldAsync()).Start);
        Assert.Equal(
            new CalendarImportSkipTally(CalendarImportSkipReason.RepeatedInTheFile, 1),
            Assert.Single(summary.Skipped));
    }

    /// <summary>The reader's own skips and the calendar's are counted in one place, so the report cannot say two different things.</summary>
    [Fact]
    public async Task SummariseAsync_EntriesTheReaderSkippedAndOneAlreadyHeld_CountsBothKindsTogether()
    {
        // Arrange
        this.store.Hold(Person, Imported("held"));
        this.reader
            .Read(Arg.Any<Stream>(), Arg.Any<TimeZoneInfo>())
            .Returns(CalendarFileReading.Read(
                [Entry("held", Monday), Entry("new", Monday.AddHours(1))],
                [CalendarImportSkipReason.Recurring, CalendarImportSkipReason.Recurring]));

        // Act
        var summary = await this.SignedIn().SummariseAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, summary.Events);
        Assert.Equal(
            [
                new CalendarImportSkipTally(CalendarImportSkipReason.Recurring, 2),
                new CalendarImportSkipTally(CalendarImportSkipReason.AlreadyOnTheCalendar, 1),
            ],
            summary.Skipped);
    }

    [Theory]
    [InlineData(CalendarImportOutcome.NotCalendarData)]
    [InlineData(CalendarImportOutcome.TooManyEntries)]
    public async Task ImportAsync_AFileRefusedAsAWhole_ReportsTheRefusalAndLeavesTheCalendarAsItWas(
        CalendarImportOutcome refusal)
    {
        // Arrange
        this.reader
            .Read(Arg.Any<Stream>(), Arg.Any<TimeZoneInfo>())
            .Returns(refusal is CalendarImportOutcome.NotCalendarData
                ? CalendarFileReading.NotCalendarData
                : CalendarFileReading.TooManyEntries);

        // Act
        var summary = await this.SignedIn().ImportAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(refusal, summary.Outcome);
        Assert.Empty(await this.HeldAsync());
    }

    /// <summary>A floating time and an all-day entry both need a zone, and whose day it is decides which one.</summary>
    [Fact]
    public async Task SummariseAsync_APersonWhoseRecordStatesAZone_ReadsTheFileInTheirOwnZoneRatherThanTheCoordinatedOne()
    {
        // Arrange
        this.zone = UserTimeZone.TryRead("Europe/Warsaw", out var warsaw) ? warsaw : throw new InvalidOperationException();
        this.Reads();

        // Act
        await this.SignedIn().SummariseAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        this.reader.Received(1).Read(Arg.Any<Stream>(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));
    }

    [Fact]
    public async Task SummariseAsync_APersonWhoseRecordStatesNoZone_ReadsTheFileInTheCoordinatedZone()
    {
        // Arrange
        this.Reads();

        // Act
        await this.SignedIn().SummariseAsync(File(), TestContext.Current.CancellationToken);

        // Assert
        this.reader.Received(1).Read(Arg.Any<Stream>(), TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task ImportAsync_ACallerWhoseGrantOmitsReadingMail_IsRefused()
    {
        // Arrange
        var import = this.ImportFor();

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => import.ImportAsync(File(), TestContext.Current.CancellationToken));
    }

    private static CalendarFileEntry Entry(string uid, DateTimeOffset start) =>
        new(
            ImportedCalendarEventUid.Create(uid),
            CalendarEventTitle.Create(uid),
            start,
            start.AddHours(1),
            IsAllDay: false);

    private static CalendarEvent Imported(string uid) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7(Now)),
            CalendarEventTitle.Create(uid),
            Monday,
            null,
            isAllDay: false,
            [],
            CalendarEventOrigin.Asserted,
            sourceMessage: null,
            ImportedCalendarEventUid.Create(uid),
            Now,
            Now);

    private static MemoryStream File() => new([1]);

    private void Reads(params CalendarFileEntry[] entries) =>
        this.reader
            .Read(Arg.Any<Stream>(), Arg.Any<TimeZoneInfo>())
            .Returns(CalendarFileReading.Read(entries, []));

    private async Task<IReadOnlyList<CalendarEvent>> HeldAsync() =>
        await this.store.ReadRangeAsync(
            Person,
            CalendarEventQuery.Create(Monday.AddYears(-1), Monday.AddYears(1), null, null),
            TestContext.Current.CancellationToken);

    private async Task<int> HeldCountAsync() => (await this.HeldAsync()).Count;

    /// <summary>Advances the fake clock in steps until a retried import has ended, and reports what it answered.</summary>
    /// <remarks>
    /// The wait between two attempts is measured on the same clock this test fixes, so an import whose commit lost a
    /// race never resumes unless that clock is moved. A step past the policy's own ceiling ends each wait whatever the
    /// jitter drew, and the guard is what ends the loop on real time so a policy that stopped making progress fails
    /// this test rather than spinning a clock nothing is waiting on.
    /// </remarks>
    private async Task<CalendarImportSummary> AdvancePastTheBackoffAsync(Task<CalendarImportSummary> import)
    {
        var guarded = import.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        while (!guarded.IsCompleted)
        {
            this.clock.Advance(TimeSpan.FromSeconds(2));

            await Task.Yield();
        }

        return await guarded;
    }

    private CalendarFileImport SignedIn() => this.ImportFor(MailFathomPermission.MailRead);

    private CalendarFileImport ImportFor(params MailFathomPermission[] granted)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory
            .BeginSessionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new StagedSession(
                this.commits.Count == 0 ? PersistenceCommitResult.Committed : this.commits.Dequeue()));

        var zones = Substitute.For<IUserTimeZones>();
        zones.ZoneOf(Arg.Any<UserId>()).Returns(_ => this.zone);

        return new CalendarFileImport(
            AccessAuthorizations.ForUserGranted(Person, granted),
            this.reader,
            this.store,
            zones,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), this.clock),
            this.clock);
    }

    /// <summary>A session that ends the way the test arranged, which is how a lost race is expressed here.</summary>
    private sealed class StagedSession(PersistenceCommitResult result) : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

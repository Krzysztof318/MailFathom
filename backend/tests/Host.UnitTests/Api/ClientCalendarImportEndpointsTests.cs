// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Calendar;
using MailFathom.Application.Calendar.Import;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the two routes an offered iCalendar file reaches a calendar over. What has to hold is that the summary
/// writes nothing, that every refusal names what a person has to change without echoing anything the file carries,
/// that a request carrying no file at all is told so rather than reported as an empty calendar, and that a file over
/// the bound is answered with the bound rather than with the pipeline's bare status.
/// </summary>
public sealed class ClientCalendarImportEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly ICalendarFileReader reader = Substitute.For<ICalendarFileReader>();
    private readonly ICalendarEventStore store = Substitute.For<ICalendarEventStore>();

    [Fact]
    public async Task SummariseAsync_AReadableFile_AnswersWhatItWouldCreateAndStagesNothing()
    {
        // Arrange
        this.Reads(CalendarFileReading.Read([Entry("one")], [CalendarImportSkipReason.Recurring]));

        // Act
        var result = await ClientCalendarImportEndpoints.SummariseAsync(
            timeZone: null,
            this.Import(),
            Offering("BEGIN:VCALENDAR"),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<CalendarImportResponse>>(result.Result).Value!;

        Assert.Equal(1, answered.Events);
        Assert.Equal(Monday, answered.Earliest);
        Assert.Equal(
            new CalendarImportSkipResponse(nameof(CalendarImportSkipReason.Recurring), 1),
            Assert.Single(answered.Skipped));

        await this.store.DidNotReceive().AddAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailUserId>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ImportAsync_AFileThatIsNotCalendarData_IsRefusedWithWhatToChooseInstead()
    {
        // Arrange
        this.Reads(CalendarFileReading.NotCalendarData);

        // Act
        var result = await ClientCalendarImportEndpoints.ImportAsync(
            timeZone: null,
            this.Import(),
            Offering("not a calendar"),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains(".ics", refused.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_AFileNamingMoreEntriesThanOneImportWrites_IsRefusedWithTheCountItMayName()
    {
        // Arrange
        this.Reads(CalendarFileReading.TooManyEntries);

        // Act
        var result = await ClientCalendarImportEndpoints.ImportAsync(
            timeZone: null,
            this.Import(),
            Offering("BEGIN:VCALENDAR"),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains(
            CalendarFileImport.MaximumEntryCount.ToString(CultureInfo.InvariantCulture),
            refused.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_AZoneThisDeploymentDoesNotKnow_IsRefusedWithTheShapeOfIdentifierItWants()
    {
        // Act
        var result = await ClientCalendarImportEndpoints.ImportAsync(
            "Mars/Olympus_Mons",
            this.Import(),
            Offering("BEGIN:VCALENDAR"),
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Contains("Europe/Warsaw", refused.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>A request with no body is somebody's client having sent nothing, which is a different thing from a calendar holding nothing.</summary>
    [Fact]
    public async Task SummariseAsync_ARequestCarryingNoFile_IsRefusedRatherThanReadAsAnEmptyCalendar()
    {
        // Act
        var result = await ClientCalendarImportEndpoints.SummariseAsync(
            timeZone: null,
            this.Import(),
            Offering(string.Empty),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        this.reader.DidNotReceive().Read(Arg.Any<Stream>(), Arg.Any<TimeZoneInfo>());
    }

    /// <summary>The pipeline's own refusal carries no text, and the bound somebody went over is the one thing they need from it.</summary>
    [Fact]
    public async Task ImportAsync_AFileOverTheBound_IsAnsweredWithTheBoundRatherThanABareStatus()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Body = new RefusingBody();

        // Act
        var result = await ClientCalendarImportEndpoints.ImportAsync(
            timeZone: null,
            this.Import(),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, refused.StatusCode);
        Assert.Contains("MB", refused.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    private static CalendarFileEntry Entry(string uid) =>
        new(
            ImportedCalendarEventUid.Create(uid),
            CalendarEventTitle.Create(uid),
            Monday,
            null,
            IsAllDay: false);

    private static DefaultHttpContext Offering(string file)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(file));

        return context;
    }

    private void Reads(CalendarFileReading reading) =>
        this.reader.Read(Arg.Any<Stream>(), Arg.Any<TimeZoneInfo>()).Returns(reading);

    private CalendarFileImport Import()
    {
        this.store
            .ReadImportedUidsAsync(
                Arg.Any<MailUserId>(),
                Arg.Any<IReadOnlyCollection<ImportedCalendarEventUid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<ImportedCalendarEventUid>());

        var clock = new FakeTimeProvider(Now);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new CalendarFileImport(
            AccessAuthorizations.ForUserGranted(SyntheticMailUser.Deployment, MailFathomPermission.MailRead),
            this.reader,
            this.store,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
            clock);
    }

    /// <summary>A body the routing pipeline has already refused, which is how an oversized upload reaches a handler.</summary>
    private sealed class RefusingBody : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge);

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A session that commits whatever was staged in it, which is what a write's ordinary path needs.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

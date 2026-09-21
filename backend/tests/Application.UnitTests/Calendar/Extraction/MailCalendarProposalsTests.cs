// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Calendar;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar.Extraction;

/// <summary>Covers what a date a message named becomes, and whose calendar it reaches.</summary>
/// <remarks>
/// What a reading finds is the agent's and is asserted where that lives. What is asserted here is the half that turns
/// one finding into rows: that every row is a proposal citing its message, that a mailbox two people are assigned
/// proposes to both, and that nothing is written for a message that named nothing.
/// </remarks>
public sealed class MailCalendarProposalsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 6, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task StageAsync_AMessageNamingADate_WritesAProposalCitingIt()
    {
        // Arrange
        var events = Substitute.For<ICalendarEventStore>();
        var owner = UserId.Create(Guid.CreateVersion7());
        var sourceMessage = StoredEmailId.Create(Guid.CreateVersion7());
        var proposals = ProposalsFor([owner], events);

        // Act
        var staged = await proposals.StageAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            sourceMessage,
            [Extracted("Racking survey")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, staged);
        await events.Received(1).AddAsync(
            Arg.Any<IPersistenceSession>(),
            owner,
            Arg.Is<CalendarEvent>(written =>
                written!.Origin == CalendarEventOrigin.Proposed
                && written.SourceMessage == sourceMessage
                && written.ImportedUid == null
                && written.RecordedAt == RecordedAt
                && written.AmendedAt == RecordedAt),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A reading of mail decides neither of the two things a person sets once they have agreed to the date, so a
    /// proposal announces nothing and states a clock time. An extraction read as a whole day would move a reminder
    /// to the morning of a date nobody has accepted yet, and one carrying reminders would announce a commitment
    /// somebody never made.
    /// </summary>
    [Fact]
    public async Task StageAsync_AMessageNamingADate_ProposesAnEventThatAnnouncesNothingAndStatesAClockTime()
    {
        // Arrange
        var events = Substitute.For<ICalendarEventStore>();
        var owner = UserId.Create(Guid.CreateVersion7());
        var proposals = ProposalsFor([owner], events);

        // Act
        await proposals.StageAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            StoredEmailId.Create(Guid.CreateVersion7()),
            [Extracted("Racking survey")],
            TestContext.Current.CancellationToken);

        // Assert
        await events.Received(1).AddAsync(
            Arg.Any<IPersistenceSession>(),
            owner,
            Arg.Is<CalendarEvent>(written => !written!.IsAllDay && written.Reminders.Count == 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>One reading is paid for and each person then decides for themselves, which is the only answer that works both ways.</summary>
    [Fact]
    public async Task StageAsync_AMailboxTwoPeopleAreAssigned_ProposesToBothCalendars()
    {
        // Arrange
        var events = Substitute.For<ICalendarEventStore>();
        var first = UserId.Create(Guid.CreateVersion7());
        var second = UserId.Create(Guid.CreateVersion7());
        var proposals = ProposalsFor([first, second], events);

        // Act
        var staged = await proposals.StageAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            StoredEmailId.Create(Guid.CreateVersion7()),
            [Extracted("Racking survey"), Extracted("Site visit")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, staged);
        await events.Received(2).AddAsync(
            Arg.Any<IPersistenceSession>(),
            first,
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>());
        await events.Received(2).AddAsync(
            Arg.Any<IPersistenceSession>(),
            second,
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mailbox nobody is assigned reaches nobody rather than everybody.</summary>
    [Fact]
    public async Task StageAsync_AMailboxAssignedToNobody_WritesNothing()
    {
        // Arrange
        var events = Substitute.For<ICalendarEventStore>();
        var proposals = ProposalsFor([], events);

        // Act
        var staged = await proposals.StageAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            StoredEmailId.Create(Guid.CreateVersion7()),
            [Extracted("Racking survey")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, staged);
        await events.DidNotReceive().AddAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<UserId>(),
            Arg.Any<CalendarEvent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message naming no date is the ordinary case, and costs neither a query nor a row.</summary>
    [Fact]
    public async Task StageAsync_AReadingThatFoundNothing_ReachesNeitherTheRelationNorTheCalendar()
    {
        // Arrange
        var events = Substitute.For<ICalendarEventStore>();
        var assignments = Substitute.For<IMailAccountAssignments>();
        var proposals = new MailCalendarProposals(
            Substitute.For<ICalendarEventExtractor>(),
            events,
            assignments,
            new FakeTimeProvider(RecordedAt));

        // Act
        var staged = await proposals.StageAsync(
            Substitute.For<IPersistenceSession>(),
            Account,
            StoredEmailId.Create(Guid.CreateVersion7()),
            [],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, staged);
        assignments.DidNotReceive().UsersAssignedTo(Arg.Any<MailAccountId>());
    }

    [Fact]
    public void IsActive_ADeploymentThatReadsNoText_SaysSoWithoutComposingAnything()
    {
        // Arrange
        var extractor = Substitute.For<ICalendarEventExtractor>();
        extractor.IsActive.Returns(false);

        // Act & Assert
        Assert.False(ProposalsFor([], Substitute.For<ICalendarEventStore>(), extractor).IsActive);
    }

    /// <summary>The reading is made outside any transaction, so it takes no session at all.</summary>
    [Fact]
    public async Task ReadAsync_AMessage_IsPutToTheReadingUnchanged()
    {
        // Arrange
        var extractor = Substitute.For<ICalendarEventExtractor>();
        var email = Enrichable();
        extractor
            .ProposeFromEmailAsync(email, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(CalendarEventExtraction.Settled([Extracted("Racking survey")])));
        var proposals = ProposalsFor([], Substitute.For<ICalendarEventStore>(), extractor);

        // Act
        var extraction = await proposals.ReadAsync(email, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Racking survey", Assert.Single(extraction.Events).Title.Value);
    }

    private static MailCalendarProposals ProposalsFor(
        IReadOnlyList<UserId> owners,
        ICalendarEventStore events,
        ICalendarEventExtractor? extractor = null)
    {
        var assignments = Substitute.For<IMailAccountAssignments>();
        assignments.UsersAssignedTo(Account).Returns(owners);

        return new MailCalendarProposals(
            extractor ?? Substitute.For<ICalendarEventExtractor>(),
            events,
            assignments,
            new FakeTimeProvider(RecordedAt));
    }

    private static ExtractedCalendarEvent Extracted(string title) =>
        new(
            CalendarEventTitle.Create(title),
            new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.Zero),
            End: null);

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking survey",
            new DateTimeOffset(2026, 9, 21, 9, 30, 0, TimeSpan.Zero),
            []);
}

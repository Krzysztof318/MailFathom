// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Calendar;

/// <summary>Covers what an event refuses to be composed as, and what accepting or amending one keeps.</summary>
public sealed class CalendarEventTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_AnEndAfterTheStart_IsTheEventsDuration()
    {
        // Act
        var held = Compose(end: Start.AddMinutes(90));

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(90), held.Duration);
    }

    /// <summary>An event that states no end says how long it lasts through nothing rather than through a zero.</summary>
    [Fact]
    public void Create_NoEnd_LeavesTheDurationUnstated()
    {
        // Act
        var held = Compose(end: null);

        // Assert
        Assert.Null(held.Duration);
    }

    /// <summary>An entry whose span is empty or reversed is a file or a reading that went wrong rather than an event to draw.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Create_AnEndNotAfterTheStart_IsRefused(int minutesFromStart)
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(end: Start.AddMinutes(minutesFromStart)));

        // Assert
        Assert.Equal("end", refusal.ParamName);
    }

    [Fact]
    public void Create_UndeclaredOrigin_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(origin: (CalendarEventOrigin)99));

        // Assert
        Assert.Equal("origin", refusal.ParamName);
    }

    /// <summary>Nothing imports a proposal, so a proposal carrying an imported identifier is a record two writers produced together.</summary>
    [Fact]
    public void Create_AProposalUnderAnImportedIdentifier_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => Compose(
            origin: CalendarEventOrigin.Proposed,
            importedUid: ImportedCalendarEventUid.Create("imported@example.test")));

        // Assert
        Assert.Equal("importedUid", refusal.ParamName);
    }

    /// <summary>Each of the three is a struct, so the one value its private constructor cannot reach is refused here.</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("importedUid")]
    public void Create_StructDefaultInPlaceOfAValidatedValue_IsRefused(string parameterName)
    {
        // Arrange
        var id = parameterName == "id" ? default : CalendarEventId.Create(Guid.CreateVersion7());
        var title = parameterName == "title" ? default : CalendarEventTitle.Create("Design review");
        ImportedCalendarEventUid? importedUid = parameterName == "importedUid"
            ? default(ImportedCalendarEventUid)
            : null;

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => CalendarEvent.Create(
            id,
            title,
            Start,
            end: null,
            CalendarEventOrigin.Asserted,
            sourceMessage: null,
            importedUid,
            RecordedAt,
            RecordedAt));

        // Assert
        Assert.Equal(parameterName, refusal.ParamName);
    }

    /// <summary>Accepting is a change to one event rather than a second one, so everything that identified it survives.</summary>
    [Fact]
    public void Accepted_AProposalFromAMessage_KeepsItsIdentityAndItsCitation()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var proposed = Compose(origin: CalendarEventOrigin.Proposed, sourceMessage: message);
        var acceptedAt = RecordedAt.AddHours(3);

        // Act
        var accepted = proposed.Accepted(acceptedAt);

        // Assert
        Assert.Equal(CalendarEventOrigin.Asserted, accepted.Origin);
        Assert.Equal(proposed.Id, accepted.Id);
        Assert.Equal(message, accepted.SourceMessage);
        Assert.Equal(proposed.RecordedAt, accepted.RecordedAt);
        Assert.Equal(acceptedAt, accepted.AmendedAt);
    }

    /// <summary>A second acceptance is a caller acting on a proposal somebody else already took.</summary>
    [Fact]
    public void Accepted_AnEventAlreadyOnTheCalendar_IsRefused()
    {
        // Arrange
        var held = Compose(origin: CalendarEventOrigin.Asserted);

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => held.Accepted(RecordedAt.AddHours(1)));
    }

    /// <summary>An amendment states the event as it is to stand, and states nothing about where it came from.</summary>
    [Fact]
    public void AmendedWith_ANewTitleAndTime_KeepsTheIdentityTheOriginAndTheImportedIdentifier()
    {
        // Arrange
        var importedUid = ImportedCalendarEventUid.Create("imported@example.test");
        var held = Compose(importedUid: importedUid);
        var movedTo = Start.AddDays(1);
        var amendedAt = RecordedAt.AddDays(1);

        // Act
        var amended = held.AmendedWith(CalendarEventTitle.Create("Design review, moved"), movedTo, end: null, amendedAt);

        // Assert
        Assert.Equal(held.Id, amended.Id);
        Assert.Equal(held.Origin, amended.Origin);
        Assert.Equal(importedUid, amended.ImportedUid);
        Assert.Equal("Design review, moved", amended.Title.Value);
        Assert.Equal(movedTo, amended.Start);
        Assert.Null(amended.End);
        Assert.Equal(amendedAt, amended.AmendedAt);
    }

    /// <summary>The amendment is held to the same invariants a composition is, rather than to the ones it remembered.</summary>
    [Fact]
    public void AmendedWith_AnEndNotAfterTheStart_IsRefused()
    {
        // Arrange
        var held = Compose();

        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() =>
            held.AmendedWith(held.Title, Start, Start, RecordedAt.AddHours(1)));

        // Assert
        Assert.Equal("end", refusal.ParamName);
    }

    private static CalendarEvent Compose(
        DateTimeOffset? end = null,
        CalendarEventOrigin origin = CalendarEventOrigin.Asserted,
        StoredEmailId? sourceMessage = null,
        ImportedCalendarEventUid? importedUid = null) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7()),
            CalendarEventTitle.Create("Design review"),
            Start,
            end,
            origin,
            sourceMessage,
            importedUid,
            RecordedAt,
            RecordedAt);
}

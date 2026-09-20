// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Infrastructure.Persistence.Calendar;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Calendar;

/// <summary>Covers what a row keeps of an event, and what reading one back refuses to answer as one.</summary>
public sealed class CalendarEventMappingTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Start = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToEntity_AnImportedEvent_KeepsEveryPartOfIt()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var held = EventOf(
            end: Start.AddHours(1),
            origin: CalendarEventOrigin.Asserted,
            sourceMessage: message,
            importedUid: ImportedCalendarEventUid.Create("entry@example.test"));

        // Act
        var row = CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment.Value, row.UserId);
        Assert.Equal(held.Id.Value, row.Id);
        Assert.Equal("Design review", row.Title);
        Assert.Equal(Start, row.StartsAt);
        Assert.Equal(Start.AddHours(1), row.EndsAt);
        Assert.Equal(CalendarEventOrigin.Asserted, row.Origin);
        Assert.Equal(message.Value, row.SourceStoredEmailId);
        Assert.Equal("entry@example.test", row.ImportedUid);
        Assert.Equal(RecordedAt, row.RecordedAt);
        Assert.Equal(RecordedAt, row.AmendedAt);
    }

    /// <summary>What an event states nothing about is absent in the row rather than written as a value of its own.</summary>
    [Fact]
    public void ToEntity_AProposalWithNoEnd_LeavesBothOptionalColumnsUnset()
    {
        // Arrange
        var held = EventOf(end: null, origin: CalendarEventOrigin.Proposed);

        // Act
        var row = CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held);

        // Assert
        Assert.Null(row.EndsAt);
        Assert.Null(row.ImportedUid);
        Assert.Null(row.SourceStoredEmailId);
    }

    /// <summary>An imported event is the only shape carrying both optional references, so it is the one a round trip has to cover.</summary>
    [Fact]
    public void ToDomain_AnImportedRowThisMappingWrote_ReadsBackWithItsIdentifier()
    {
        // Arrange
        var importedUid = ImportedCalendarEventUid.Create("entry@example.test");
        var held = EventOf(end: Start.AddHours(1), origin: CalendarEventOrigin.Asserted, importedUid: importedUid);

        // Act
        var read = CalendarEventMapping.ToDomain(CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held));

        // Assert
        Assert.Equal(importedUid, read.ImportedUid);
        Assert.Equal(held.Id, read.Id);
        Assert.Equal(held.End, read.End);
    }

    [Fact]
    public void ToDomain_ARowThisMappingWrote_ReadsBackAsTheSameEvent()
    {
        // Arrange
        var held = EventOf(
            end: Start.AddHours(1),
            origin: CalendarEventOrigin.Proposed,
            sourceMessage: StoredEmailId.Create(Guid.CreateVersion7()));

        // Act
        var read = CalendarEventMapping.ToDomain(CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held));

        // Assert
        Assert.Equal(held.Id, read.Id);
        Assert.Equal(held.Title, read.Title);
        Assert.Equal(held.Start, read.Start);
        Assert.Equal(held.End, read.End);
        Assert.Equal(held.Origin, read.Origin);
        Assert.Equal(held.SourceMessage, read.SourceMessage);
        Assert.Equal(held.RecordedAt, read.RecordedAt);
        Assert.Equal(held.AmendedAt, read.AmendedAt);
    }

    /// <summary>
    /// The instant a reminder falls at is written beside the lead so the pass announcing them asks the database which
    /// have come due rather than reading every calendar to find out.
    /// </summary>
    [Fact]
    public void ToEntity_AnEventCarryingReminders_WritesEachLeadWithTheInstantItFallsAt()
    {
        // Arrange
        var held = EventOf(
            end: null,
            origin: CalendarEventOrigin.Asserted,
            reminders: [Reminder.Create(15), Reminder.Create(60)]);

        // Act
        var row = CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held);

        // Assert
        Assert.Equal([60, 15], row.Reminders.Select(reminder => reminder.MinutesBefore));
        Assert.Equal([Start.AddHours(-1), Start.AddMinutes(-15)], row.Reminders.Select(reminder => reminder.DueAt));
        Assert.All(row.Reminders, reminder => Assert.Null(reminder.RaisedForDueAt));
    }

    /// <summary>A day is announced back from the morning of it, which is the event's rule and therefore the row's.</summary>
    [Fact]
    public void ToEntity_ADayRatherThanAClockTime_MeasuresTheRowsInstantFromThatMorning()
    {
        // Arrange
        var held = EventOf(
            end: null,
            origin: CalendarEventOrigin.Asserted,
            isAllDay: true,
            reminders: [Reminder.Create(0)]);

        // Act
        var row = CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held);

        // Assert
        Assert.True(row.IsAllDay);
        Assert.Equal(
            new DateTimeOffset(Start.Date.AddHours(CalendarEvent.AllDayReminderHour), Start.Offset),
            Assert.Single(row.Reminders).DueAt);
    }

    [Fact]
    public void ToDomain_ARowCarryingReminders_ReadsThemBackAsTheEventStatedThem()
    {
        // Arrange
        var held = EventOf(
            end: null,
            origin: CalendarEventOrigin.Asserted,
            isAllDay: true,
            reminders: [Reminder.Create(15), Reminder.Create(1440)]);

        // Act
        var read = CalendarEventMapping.ToDomain(CalendarEventMapping.ToEntity(SyntheticMailUser.Deployment, held));

        // Assert
        Assert.True(read.IsAllDay);
        Assert.Equal(held.Reminders, read.Reminders);
    }

    /// <summary>
    /// A row is input from outside this process however it got there, so one the schema admits and the calendar does
    /// not is refused rather than answered as an event.
    /// </summary>
    [Fact]
    public void ToDomain_ARowTheCalendarWouldNotHaveWritten_IsRefused()
    {
        // Arrange
        var row = new CalendarEventEntity
        {
            Id = Guid.CreateVersion7(),
            UserId = SyntheticMailUser.Deployment.Value,
            Title = "Design review",
            StartsAt = Start,
            EndsAt = Start.AddHours(-1),
            Origin = CalendarEventOrigin.Asserted,
            RecordedAt = RecordedAt,
            AmendedAt = RecordedAt,
        };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarEventMapping.ToDomain(row));
    }

    private static CalendarEvent EventOf(
        DateTimeOffset? end,
        CalendarEventOrigin origin,
        StoredEmailId? sourceMessage = null,
        ImportedCalendarEventUid? importedUid = null,
        bool isAllDay = false,
        IReadOnlyCollection<Reminder>? reminders = null) =>
        CalendarEvent.Create(
            CalendarEventId.Create(Guid.CreateVersion7()),
            CalendarEventTitle.Create("Design review"),
            Start,
            end,
            isAllDay,
            reminders ?? [],
            origin,
            sourceMessage,
            importedUid,
            RecordedAt,
            RecordedAt);
}

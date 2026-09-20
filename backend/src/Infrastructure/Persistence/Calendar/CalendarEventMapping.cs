// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Calendar;

/// <summary>Turns an event between the calendar's own record and the row that holds it.</summary>
/// <remarks>
/// A stored row is input from outside this process however it got there, so reading one back builds the domain record
/// through the same factory a writer goes through: a row whose title, times, or origin the schema would admit but the
/// calendar would not is refused here rather than answered as an event.
/// </remarks>
internal static class CalendarEventMapping
{
    internal static CalendarEventEntity ToEntity(MailUserId owner, CalendarEvent calendarEvent)
    {
        var entity = new CalendarEventEntity
        {
            Id = calendarEvent.Id.Value,
            UserId = owner.Value,
            Title = calendarEvent.Title.Value,
            StartsAt = calendarEvent.Start,
            EndsAt = calendarEvent.End,
            IsAllDay = calendarEvent.IsAllDay,
            Origin = calendarEvent.Origin,
            SourceStoredEmailId = calendarEvent.SourceMessage?.Value,
            ImportedUid = calendarEvent.ImportedUid?.Value,
            RecordedAt = calendarEvent.RecordedAt,
            AmendedAt = calendarEvent.AmendedAt,
        };

        foreach (var reminder in calendarEvent.Reminders)
        {
            entity.Reminders.Add(ToEntity(calendarEvent, reminder));
        }

        return entity;
    }

    /// <summary>Builds the row one reminder of an event is stored as, with the instant it currently falls at.</summary>
    /// <remarks>
    /// The instant is derived from the event rather than from the reminder, because the anchor an all-day event is
    /// measured back from is the event's own rule. It is written rather than computed on read so that the pass
    /// announcing reminders can ask the database which have come due instead of reading every calendar to find out.
    /// </remarks>
    internal static CalendarEventReminderEntity ToEntity(CalendarEvent calendarEvent, CalendarReminder reminder) =>
        new()
        {
            CalendarEventId = calendarEvent.Id.Value,
            MinutesBefore = reminder.MinutesBefore,
            DueAt = calendarEvent.RemindsAt(reminder),
        };

    internal static CalendarEvent ToDomain(CalendarEventEntity stored) =>
        CalendarEvent.Create(
            CalendarEventId.Create(stored.Id),
            CalendarEventTitle.Create(stored.Title),
            stored.StartsAt,
            stored.EndsAt,
            stored.IsAllDay,
            [.. stored.Reminders.Select(reminder => CalendarReminder.Create(reminder.MinutesBefore))],
            stored.Origin,
            stored.SourceStoredEmailId is { } message ? StoredEmailId.Create(message) : null,
            stored.ImportedUid is { } uid ? ImportedCalendarEventUid.Create(uid) : null,
            stored.RecordedAt,
            stored.AmendedAt);
}

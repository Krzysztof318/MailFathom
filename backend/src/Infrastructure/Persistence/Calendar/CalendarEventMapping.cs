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
    internal static CalendarEventEntity ToEntity(MailUserId owner, CalendarEvent calendarEvent) =>
        new()
        {
            Id = calendarEvent.Id.Value,
            UserId = owner.Value,
            Title = calendarEvent.Title.Value,
            StartsAt = calendarEvent.Start,
            EndsAt = calendarEvent.End,
            Origin = calendarEvent.Origin,
            SourceStoredEmailId = calendarEvent.SourceMessage?.Value,
            ImportedUid = calendarEvent.ImportedUid?.Value,
            RecordedAt = calendarEvent.RecordedAt,
            AmendedAt = calendarEvent.AmendedAt,
        };

    internal static CalendarEvent ToDomain(CalendarEventEntity stored) =>
        CalendarEvent.Create(
            CalendarEventId.Create(stored.Id),
            CalendarEventTitle.Create(stored.Title),
            stored.StartsAt,
            stored.EndsAt,
            stored.Origin,
            stored.SourceStoredEmailId is { } message ? StoredEmailId.Create(message) : null,
            stored.ImportedUid is { } uid ? ImportedCalendarEventUid.Create(uid) : null,
            stored.RecordedAt,
            stored.AmendedAt);
}

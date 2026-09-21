// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Reminders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Reminders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Calendar;

/// <summary>Answers what has come due across every calendar, and records what a pass announced.</summary>
/// <remarks>
/// <para>
/// One of the readers here that names no owner, because a reminder comes due whether or not anybody is signed in. It
/// is still bounded in every direction a table scan could go: a window with both ends, a stated limit, and an index
/// that holds only the reminders nothing has announced yet.
/// </para>
/// <para>
/// The claim is a conditional update rather than a read followed by a write. Two replicas reading one pass together
/// is the ordinary case, so the loser is told it changed no row and announces nothing further, and an event moved
/// between the read and the claim fails the same comparison — which is right, because the reminder it read is no
/// longer the one the calendar holds.
/// </para>
/// <para>
/// Nothing logs. A title says who somebody is meeting and the instants say when they are committed, so what a
/// failure carries is the event's identifier.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CalendarReminderSchedule(MailFathomDbContext context) : IReminderSchedule
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DueReminder>> ReadDueAsync(
        DateTimeOffset asOf,
        DateTimeOffset notDueBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var due = await context.CalendarEventReminders
            .AsNoTracking()
            .Where(reminder =>
                reminder.RaisedForDueAt == null
                && reminder.DueAt <= asOf
                && reminder.DueAt >= notDueBefore)
            .OrderBy(reminder => reminder.DueAt)
            .ThenBy(reminder => reminder.CalendarEventId)
            .ThenBy(reminder => reminder.MinutesBefore)
            .Take(limit)
            .Select(reminder => new
            {
                reminder.CalendarEvent!.UserId,
                reminder.CalendarEventId,
                reminder.CalendarEvent!.Title,
                reminder.MinutesBefore,
                reminder.DueAt,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. due.Select(reminder => new DueReminder(
                UserId.Create(reminder.UserId),
                ReminderSubject.CalendarEvent,
                reminder.CalendarEventId,
                reminder.Title,
                Reminder.Create(reminder.MinutesBefore),
                reminder.DueAt)),
        ];
    }

    /// <inheritdoc />
    public async Task<bool> MarkRaisedAsync(DueReminder due, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);

        var eventValue = due.Identity;
        var minutesBefore = due.Reminder.MinutesBefore;
        var dueAt = due.DueAt;

        var claimed = await context.CalendarEventReminders
            .Where(reminder =>
                reminder.CalendarEventId == eventValue
                && reminder.MinutesBefore == minutesBefore
                && reminder.RaisedForDueAt == null
                && reminder.DueAt == dueAt)
            .ExecuteUpdateAsync(
                update => update.SetProperty(reminder => reminder.RaisedForDueAt, dueAt),
                cancellationToken);

        return claimed > 0;
    }
}

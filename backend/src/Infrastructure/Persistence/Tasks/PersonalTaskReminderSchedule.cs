// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Reminders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Reminders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Tasks;

/// <summary>Answers what has come due across every task list, and records what a pass announced.</summary>
/// <remarks>
/// <para>
/// The calendar's reader with one condition of its own: <b>a task somebody has already done raises nothing.</b> The
/// completion is read through the task rather than copied onto the reminder, so finishing something early silences
/// it without any write reaching these rows — and un-finishing it inside the hour a pass still looks back over
/// brings it back, which is the state the person put it in.
/// </para>
/// <para>
/// It names no owner, because a reminder comes due whether or not anybody is signed in. It is still bounded in
/// every direction a table scan could go: a window with both ends, a stated limit, and an index that holds only the
/// reminders nothing has announced yet.
/// </para>
/// <para>
/// The claim is a conditional update rather than a read followed by a write. Two replicas reading one pass together
/// is the ordinary case, so the loser is told it changed no row and announces nothing further, and a due date moved
/// between the read and the claim fails the same comparison — which is right, because the reminder it read is no
/// longer the one the list holds.
/// </para>
/// <para>
/// Nothing logs. A title may be a sentence read out of somebody's mail, so what a failure carries is the task's
/// identifier.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersonalTaskReminderSchedule(MailFathomDbContext context) : IReminderSchedule
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DueReminder>> ReadDueAsync(
        DateTimeOffset asOf,
        DateTimeOffset notDueBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var due = await context.PersonalTaskReminders
            .AsNoTracking()
            .Where(reminder =>
                reminder.RaisedForDueAt == null
                && reminder.DueAt <= asOf
                && reminder.DueAt >= notDueBefore
                && !reminder.PersonalTask!.IsCompleted)
            .OrderBy(reminder => reminder.DueAt)
            .ThenBy(reminder => reminder.PersonalTaskId)
            .ThenBy(reminder => reminder.MinutesBefore)
            .Take(limit)
            .Select(reminder => new
            {
                reminder.PersonalTask!.UserId,
                reminder.PersonalTaskId,
                reminder.PersonalTask!.Title,
                reminder.MinutesBefore,
                reminder.DueAt,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. due.Select(reminder => new DueReminder(
                MailUserId.Create(reminder.UserId),
                ReminderSubject.PersonalTask,
                reminder.PersonalTaskId,
                reminder.Title,
                Reminder.Create(reminder.MinutesBefore),
                reminder.DueAt)),
        ];
    }

    /// <inheritdoc />
    public async Task<bool> MarkRaisedAsync(DueReminder due, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(due);

        var taskValue = due.Identity;
        var minutesBefore = due.Reminder.MinutesBefore;
        var dueAt = due.DueAt;

        var claimed = await context.PersonalTaskReminders
            .Where(reminder =>
                reminder.PersonalTaskId == taskValue
                && reminder.MinutesBefore == minutesBefore
                && reminder.RaisedForDueAt == null
                && reminder.DueAt == dueAt)
            .ExecuteUpdateAsync(
                update => update.SetProperty(reminder => reminder.RaisedForDueAt, dueAt),
                cancellationToken);

        return claimed > 0;
    }
}

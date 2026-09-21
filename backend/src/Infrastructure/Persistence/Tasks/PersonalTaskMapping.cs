// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Tasks;

/// <summary>Turns a task into the row it is stored as.</summary>
internal static class PersonalTaskMapping
{
    /// <summary>Maps one stored row back onto the task it holds.</summary>
    /// <param name="entity">The row read back.</param>
    /// <returns>The task.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entity" /> is <see langword="null" />.</exception>
    public static PersonalTask ToPersonalTask(PersonalTaskEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return PersonalTask.Restore(
            PersonalTaskId.Create(entity.Id),
            UserId.Create(entity.UserId),
            entity.Title,
            entity.DueOn,
            ToAnnouncement(entity),
            entity.Origin,
            entity.SourceStoredEmailId is { } message ? StoredEmailId.Create(message) : null,
            entity.IsCompleted);
    }

    /// <summary>Maps one task onto the row it is written as.</summary>
    /// <param name="task">The task to store.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="task" /> is <see langword="null" />.</exception>
    public static PersonalTaskEntity ToEntity(PersonalTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var entity = new PersonalTaskEntity
        {
            Id = task.Id.Value,
            UserId = task.User.Value,
            Title = task.Title,
            DueOn = task.DueOn,
            DueDayOffsetMinutes = OffsetMinutesOf(task),
            Origin = task.Origin,
            IsCompleted = task.IsCompleted,
            SourceStoredEmailId = task.SourceMessage?.Value,
        };

        foreach (var reminder in task.Reminders)
        {
            entity.Reminders.Add(ToEntity(task, reminder));
        }

        return entity;
    }

    /// <summary>Builds the row one reminder of a task is stored as, with the instant it currently falls at.</summary>
    /// <remarks>
    /// The instant is derived from the task rather than from the lead, because the hour a due day is measured back
    /// from and the offset it is read in are both the task's. It is written rather than computed on read so that the
    /// pass announcing reminders can ask the database which have come due instead of reading every list to find out.
    /// </remarks>
    public static PersonalTaskReminderEntity ToEntity(PersonalTask task, Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new PersonalTaskReminderEntity
        {
            PersonalTaskId = task.Id.Value,
            MinutesBefore = reminder.MinutesBefore,
            DueAt = task.RemindsAt(reminder),
        };
    }

    /// <summary>Reads what a stored row says announces the task.</summary>
    /// <remarks>
    /// A row carrying leads and no offset is one an older build wrote or one somebody edited by hand, and it names
    /// no instant — so it is restored as announcing nothing rather than as reminders measured from an hour this
    /// deployment invented.
    /// </remarks>
    private static TaskAnnouncement ToAnnouncement(PersonalTaskEntity entity) =>
        entity.DueDayOffsetMinutes is { } offsetMinutes && entity.Reminders.Count > 0
            ? new TaskAnnouncement(
                TimeSpan.FromMinutes(offsetMinutes),
                [.. entity.Reminders.Select(reminder => Reminder.Create(reminder.MinutesBefore))])
            : TaskAnnouncement.Silent;

    /// <summary>Writes the offset a task states as the whole minutes the column holds.</summary>
    private static int? OffsetMinutesOf(PersonalTask task) =>
        task.DueDayOffset is { } offset ? (int)offset.TotalMinutes : null;
}

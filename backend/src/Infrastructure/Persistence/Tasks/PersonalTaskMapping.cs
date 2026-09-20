// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
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
            MailUserId.Create(entity.UserId),
            entity.Title,
            entity.DueOn,
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

        return new PersonalTaskEntity
        {
            Id = task.Id.Value,
            UserId = task.User.Value,
            Title = task.Title,
            DueOn = task.DueOn,
            Origin = task.Origin,
            IsCompleted = task.IsCompleted,
            SourceStoredEmailId = task.SourceMessage?.Value,
        };
    }
}

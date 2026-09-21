// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Tasks.Configurations;

/// <summary>Declares the reminders a task carries, and the one index the run that announces them reads.</summary>
/// <remarks>
/// <para>
/// The key is the task and the lead together, because a lead is what a person set rather than something with an
/// identity of its own: asking to be reminded a day beforehand twice is one reminder, and the key is what says so in
/// the database rather than only in the record that writes it.
/// </para>
/// <para>
/// The row cascades from the task, which is what deleting a task promises a person: what is gone raises nothing, and
/// the run that announces reminders has nothing left to find.
/// </para>
/// <para>
/// One index carries the whole of the producer's query, and it is partial on the claim being absent: an announced
/// reminder is one no pass reads again, and a due date somebody moved clears the claim in the same write, so the
/// index holds the unannounced ones alone and leaves out every reminder of every task already past.
/// </para>
/// </remarks>
internal sealed class PersonalTaskReminderConfiguration : IEntityTypeConfiguration<PersonalTaskReminderEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PersonalTaskReminderEntity> entity)
    {
        entity.ToTable("task_reminders");
        entity.HasKey(reminder => new { reminder.PersonalTaskId, reminder.MinutesBefore })
            .HasName(PersistenceConstraintNames.PersonalTaskReminderPrimaryKeyConstraintName);

        entity.Property(reminder => reminder.MinutesBefore).IsRequired();
        entity.Property(reminder => reminder.DueAt).IsRequired();

        entity.HasIndex(reminder => reminder.DueAt)
            .HasFilter($"\"{nameof(PersonalTaskReminderEntity.RaisedForDueAt)}\" IS NULL")
            .HasDatabaseName(PersistenceConstraintNames.PersonalTaskReminderDueIndexName);

        entity.HasOne(reminder => reminder.PersonalTask)
            .WithMany(task => task.Reminders)
            .HasForeignKey(reminder => reminder.PersonalTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

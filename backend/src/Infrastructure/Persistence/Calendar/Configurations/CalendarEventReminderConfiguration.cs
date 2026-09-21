// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Calendar.Configurations;

/// <summary>Declares the reminders an event carries, and the one index the run that announces them reads.</summary>
/// <remarks>
/// <para>
/// The key is the event and the lead together, because a lead is what a person set rather than something with an
/// identity of its own: asking for a quarter of an hour beforehand twice is one reminder, and the key is what says
/// so in the database rather than only in the record that writes it.
/// </para>
/// <para>
/// The row cascades from the event, which is what the delete confirmation promises a person: an event deleted takes
/// its reminders with it, and the run that announces them has nothing left to find.
/// </para>
/// <para>
/// One index carries the whole of the producer's query, and it is partial on the claim being absent: an announced
/// reminder is one no pass reads again, and a move that makes one due again clears the claim in the same write, so
/// the index holds the unannounced ones alone and leaves out every reminder of every event already past.
/// </para>
/// </remarks>
internal sealed class CalendarEventReminderConfiguration : IEntityTypeConfiguration<CalendarEventReminderEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CalendarEventReminderEntity> entity)
    {
        entity.ToTable("calendar_event_reminders");
        entity.HasKey(reminder => new { reminder.CalendarEventId, reminder.MinutesBefore })
            .HasName(PersistenceConstraintNames.CalendarEventReminderPrimaryKeyConstraintName);

        entity.Property(reminder => reminder.MinutesBefore).IsRequired();
        entity.Property(reminder => reminder.DueAt).IsRequired();

        entity.HasIndex(reminder => reminder.DueAt)
            .HasFilter($"\"{nameof(CalendarEventReminderEntity.RaisedForDueAt)}\" IS NULL")
            .HasDatabaseName(PersistenceConstraintNames.CalendarEventReminderDueIndexName);

        entity.HasOne(reminder => reminder.CalendarEvent)
            .WithMany(calendarEvent => calendarEvent.Reminders)
            .HasForeignKey(reminder => reminder.CalendarEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

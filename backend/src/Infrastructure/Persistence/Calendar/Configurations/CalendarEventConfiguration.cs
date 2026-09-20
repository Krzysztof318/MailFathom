// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Calendar.Configurations;

/// <summary>Declares the events one person's calendar holds, whether they put them there or mail proposed them.</summary>
/// <remarks>
/// <para>
/// The owner is a column every read leads with rather than part of the identity, because an event keeps one identity
/// while being accepted and amended, and the key onto the user record cascades so that erasing a person takes their
/// calendar rather than leaving it behind.
/// </para>
/// <para>
/// The message an event cites is a foreign key that clears rather than cascades, which is the one place this table
/// disagrees with the tables recording what was done about a message. Those are records of an act and go with the mail
/// they were about; an event is a person's own plan, and erasing the message a date was found in is not a reason to
/// take the meeting off their calendar. What goes is the pointer, so the event stays and the thread simply cannot be
/// opened from it any more.
/// </para>
/// <para>
/// The imported identifier is unique per calendar and only where it is present, which is what makes importing one file
/// twice create nothing the second time — a rule in the database rather than a read before the insert, because two
/// imports running at once both read nothing. It is partial so that the events nobody imported, which is nearly all of
/// them, cost no index entry.
/// </para>
/// <para>
/// The origin is held as its own name for the reason every bounded value beside it is, and the concurrency token is
/// there because an event is amended in place — retitled, moved, accepted — so an amendment written from state read
/// earlier has to fail rather than win.
/// </para>
/// </remarks>
internal sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEventEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CalendarEventEntity> entity)
    {
        entity.ToTable("calendar_events");
        entity.HasKey(calendarEvent => calendarEvent.Id);
        entity.Property(calendarEvent => calendarEvent.Id).ValueGeneratedNever();

        entity.Property(calendarEvent => calendarEvent.Title)
            .HasMaxLength(CalendarEventEntity.MaximumTitleLength)
            .IsRequired();
        entity.Property(calendarEvent => calendarEvent.StartsAt).IsRequired();
        entity.Property(calendarEvent => calendarEvent.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(calendarEvent => calendarEvent.ImportedUid)
            .HasMaxLength(CalendarEventEntity.MaximumImportedUidLength);
        entity.Property(calendarEvent => calendarEvent.ConcurrencyVersion).IsRowVersion();

        // The order every window is read in, and the one a view over the calendar draws. The owner leads it because a
        // read is always one person's, the start follows because a window is a range over it, and the identity settles
        // two events beginning at the same instant so the order is total and a window answers the same way twice.
        entity.HasIndex(calendarEvent => new
        {
            calendarEvent.UserId,
            calendarEvent.StartsAt,
            calendarEvent.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.CalendarEventWindowIndexName);

        entity.HasIndex(calendarEvent => new { calendarEvent.UserId, calendarEvent.ImportedUid })
            .IsUnique()
            .HasFilter($"\"{nameof(CalendarEventEntity.ImportedUid)}\" IS NOT NULL")
            .HasDatabaseName(PersistenceConstraintNames.CalendarEventImportedUidUniqueIndexName);

        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(calendarEvent => calendarEvent.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(calendarEvent => calendarEvent.SourceStoredEmailId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

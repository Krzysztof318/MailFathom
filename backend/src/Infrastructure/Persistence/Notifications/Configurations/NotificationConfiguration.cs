// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Notifications;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Notifications.Configurations;

/// <summary>Declares what a person is told about, and the two ways a row leaves again.</summary>
/// <remarks>
/// <para>
/// Two cascades reach this table and they answer different obligations. The owner's own row takes their notifications
/// with them, so an erasure request never has to know this table exists; and a stored message takes the notifications
/// that lead to it, so nothing can leave a row pointing at mail that is gone. That second one is the whole reason the
/// message is an association here where the audit trails beside this table deliberately keep theirs as a value: a
/// trail records an act and has to outlive what it acted on, while a notification only offers to open something.
/// </para>
/// <para>
/// Nothing here is mail content. A title and a body are derived when the notification is produced and are MailFathom's
/// own sentences about a count or a condition; the source is an account identifier; and the deduplication key is a
/// condition's name. What makes the row personal data is that it says something reached this person's mailbox and
/// when, which is what the retention bound and both cascades answer for.
/// </para>
/// </remarks>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationEntity> entity)
    {
        entity.ToTable("notifications");
        entity.HasKey(notification => notification.Id);
        entity.Property(notification => notification.Id).ValueGeneratedNever();

        // Stored as text for the reason every other enum in this model is: all three stay readable in an ad-hoc query
        // and survive any later reordering of their enum.
        entity.Property(notification => notification.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(notification => notification.TargetKind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(notification => notification.TargetScreen).HasConversion<string>().HasMaxLength(64);

        entity.Property(notification => notification.Title)
            .HasMaxLength(Notification.MaximumTitleLength)
            .IsRequired();
        entity.Property(notification => notification.Body)
            .HasMaxLength(Notification.MaximumBodyLength)
            .IsRequired();
        entity.Property(notification => notification.Source).HasMaxLength(Notification.MaximumSourceLength);
        entity.Property(notification => notification.DeduplicationKey)
            .HasMaxLength(NotificationDeduplicationKey.MaximumLength)
            .IsRequired();

        // Cascade rather than a statement in the erasure walk, for the reason the client's preferences cascade: what a
        // person was told is derived from them and goes when they do. It is also what makes the row reachable at all
        // by an erasure, since this table names no mail account and the walk enumerates the tables that do.
        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(notification => notification.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Optional, because most notifications lead to a screen or to nothing at all, and cascading so that the one
        // shape that does name a message cannot outlive it.
        entity.HasOne(notification => notification.TargetStoredEmail)
            .WithMany()
            .HasForeignKey(notification => notification.TargetStoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        // The deduplication rule, in the database rather than before the insert: a raise repeated while the first is
        // still unread passes any application check, and only the constraint closes that window. It is partial so that
        // reading the notification frees the condition to be said again when it recurs — and being partial is what
        // also makes it the index the unread count is answered from, since the count is one owner's rows in it.
        entity.HasIndex(notification => new { notification.OwnerId, notification.DeduplicationKey })
            .IsUnique()
            .HasFilter($"NOT \"{nameof(NotificationEntity.IsRead)}\"")
            .HasDatabaseName(PersistenceConstraintNames.NotificationUnreadConditionUniqueIndexName);

        // The one index the centre is walked through, and it serves both readers: a page is one owner's notifications
        // newest first, and retention erases the same owner's oldest.
        entity.HasIndex(notification => new
        {
            notification.OwnerId,
            notification.OccurredAt,
            notification.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.NotificationTimelineIndexName);
    }
}

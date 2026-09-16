// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares where the server still holds a message a delete MailFathom authored only flagged <c>\Deleted</c>.</summary>
/// <remarks>
/// <para>
/// Such a delete disposes of the local copy while the server goes on holding the message, and removing the flag there
/// undoes the delete. Synchronization has to keep asking about the occurrence for that reason, and neither record that
/// could say where it is survives in every case: the stored row is gone once the local copy is erased, and the mutation
/// record is removed with it. So the settlement writes this record in the same transaction, and the reconciliation of
/// the folder reads it until the server expunges the message or removes the flag.
/// </para>
/// <para>
/// What it holds is a folder, a UIDVALIDITY, a UID, and the local email where one was kept — the server's own names for
/// a position in a mailbox and MailFathom's own identity for a row, and no part of the message.
/// </para>
/// </remarks>
internal sealed class MailboxFlaggedDeleteConfiguration : IEntityTypeConfiguration<MailboxFlaggedDeleteEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxFlaggedDeleteEntity> entity)
    {
        entity.ToTable("mailbox_flagged_deletes");
        entity.HasKey(flagged => flagged.Id);
        entity.Property(flagged => flagged.Id).ValueGeneratedNever();
        entity.Property(flagged => flagged.MailboxAccountId).HasMaxLength(128);

        // Unique on the occurrence, because a settlement replayed after a commit conflict follows the occurrence once.
        entity.HasIndex(flagged => new { flagged.MailFolderId, flagged.UidValidity, flagged.Uid })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxFlaggedDeleteOccurrenceUniqueIndexName);

        // The order a folder's run asks about them in, which is longest unread first so a bounded run reaches each.
        entity.HasIndex(flagged => new { flagged.MailFolderId, flagged.UidValidity, flagged.LastObservedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailboxFlaggedDeleteQueueIndexName);

        // Cascades from the folder for the reason a source removal does: a record naming a folder that is gone names
        // nothing a run could ask about.
        entity.HasOne<MailFolderEntity>()
            .WithMany()
            .HasForeignKey(flagged => flagged.MailFolderId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(flagged => flagged.StoredEmailId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares where the source still holds a message MailFathom no longer does.</summary>
/// <remarks>
/// <para>
/// A held account's erasure removes the local row, and the source copy has to go with it — but the erasing request
/// must not wait on a mail server, and the row that would have said where the message was is exactly what the erasure
/// deletes. So the erasure writes this record in the same transaction, and the drain removes the source copy
/// afterwards under the account's own lease. It is the one place a UID outlives the mail it named.
/// </para>
/// <para>
/// Deliberately not a <c>mailbox_mutations</c> row. The converger already consumes a held account's delete records and
/// would take one of these for a change to issue and complete again, which is a loop rather than a reuse.
/// </para>
/// <para>
/// What it holds is a folder, a UIDVALIDITY, and a UID — the server's own names for a position in a mailbox, and no
/// part of the message. That is what lets the record survive the erasure of the mail it points at without being a
/// second copy of it.
/// </para>
/// </remarks>
internal sealed class MailboxSourceRemovalConfiguration : IEntityTypeConfiguration<MailboxSourceRemovalEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxSourceRemovalEntity> entity)
    {
        entity.ToTable("mailbox_source_removals");
        entity.HasKey(removal => removal.Id);
        entity.Property(removal => removal.Id).ValueGeneratedNever();
        entity.Property(removal => removal.MailboxAccountId).HasMaxLength(128);

        // Unique on the occurrence, because the erasure that writes it may be attempted again and the source copy is
        // removed once whether it was recorded once or twice.
        entity.HasIndex(removal => new { removal.MailFolderId, removal.UidValidity, removal.Uid })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxSourceRemovalOccurrenceUniqueIndexName);

        // The order a pass takes them in, which is oldest first so the backlog an erasure left drains from its end.
        entity.HasIndex(removal => new { removal.MailboxAccountId, removal.RecordedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailboxSourceRemovalQueueIndexName);

        // Cascades from the folder for the reason the mutation record does: an account's erasure removes its folders
        // in one statement, and a record naming a folder that is gone names nothing the drain could act on.
        entity.HasOne(removal => removal.MailFolder)
            .WithMany()
            .HasForeignKey(removal => removal.MailFolderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

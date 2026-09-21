// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares the appends a restore issued to put a held mailbox back onto its source.</summary>
/// <remarks>
/// <para>
/// One row per message rather than per attempt, and the uniqueness is the safety property rather than a tidiness: an
/// <c>APPEND</c> issued twice is a second message in somebody's folder, and nothing the folder shows afterwards tells
/// the two apart. The row is written before the command and deleted once the server names where the copy went, so a
/// row that exists at all says the outcome is unknown until an operator settles it.
/// </para>
/// <para>
/// Deliberately not a <c>mailbox_mutations</c> row. The converger issues and completes what it finds there, and an
/// append is the one command it must never reissue — a record it could take in hand would be exactly the loop this
/// table exists to prevent.
/// </para>
/// <para>
/// What it holds is a message identity, a folder alias, and two instants. No path, no UID, and nothing derived from
/// the message, because what an operator needs from it is which of their folders to look in and which record to
/// settle.
/// </para>
/// </remarks>
internal sealed class MailboxRestoreAppendConfiguration : IEntityTypeConfiguration<MailboxRestoreAppendEntity>
{
    /// <summary>What the unanswered index is filtered to, which is exactly the records that hold an account in its phase.</summary>
    private const string UnansweredFilter = $"\"{nameof(MailboxRestoreAppendEntity.SettledAt)}\" IS NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxRestoreAppendEntity> entity)
    {
        entity.ToTable("mailbox_restore_appends");
        entity.HasKey(append => append.Id);
        entity.Property(append => append.Id).ValueGeneratedNever();
        entity.Property(append => append.MailboxAccountId).HasMaxLength(128);
        entity.Property(append => append.FolderAlias).HasMaxLength(128);

        // One record per message, settled or not, because a settled one is the standing statement that the source
        // already holds a copy the message has no occurrence for.
        entity.HasIndex(append => append.StoredEmailId)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxRestoreAppendEmailUniqueIndexName);

        // The reading an operator takes and the condition the phase ends on, filtered to the rows either can act on:
        // a settled record never appears in it again, however long the account keeps the message.
        entity.HasIndex(append => new { append.MailboxAccountId, append.IssuedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailboxRestoreAppendUnansweredIndexName)
            .HasFilter(UnansweredFilter);

        // Cascades from the message for the reason every record derived from one does: erasing the mail erases what
        // MailFathom recorded about where its copy went, and a record naming a message that is gone names nothing the
        // restore could append.
        entity.HasOne(append => append.StoredEmail)
            .WithMany()
            .HasForeignKey(append => append.StoredEmailId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Exports.Configurations;

/// <summary>Declares the exports a deployment has been asked for, and the two orders they are read in.</summary>
/// <remarks>
/// The rows go with the account, because an export is a copy of that account's mail and an erased account must leave no
/// record pointing at an archive of it. The archive object itself is removed by the deletion path that removes the row,
/// and anything that path misses is an orphan the content reclamation sweeps.
/// </remarks>
internal sealed class MailboxExportConfiguration : IEntityTypeConfiguration<MailboxExportEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxExportEntity> entity)
    {
        entity.ToTable("mailbox_exports");
        entity.HasKey(export => export.Id)
            .HasName(PersistenceConstraintNames.MailboxExportPrimaryKeyConstraintName);

        entity.Property(export => export.MailboxAccountId)
            .HasMaxLength(128)
            .IsRequired();

        entity.Property(export => export.FolderPath)
            .HasMaxLength(MailboxExportEntity.MaximumFolderPathLength);

        // Written as text for the reason every other state column in this schema is: an operator reading the row while
        // answering a question should see what the export is doing rather than which ordinal it was declared at.
        entity.Property(export => export.State).HasConversion<string>().HasMaxLength(32).IsRequired();

        entity.Property(export => export.ObjectLocator)
            .HasMaxLength(MailboxExportEntity.MaximumObjectLocatorLength);

        entity.HasOne(export => export.MailboxAccount)
            .WithMany()
            .HasForeignKey(export => export.MailboxAccountId)
            .HasConstraintName(PersistenceConstraintNames.MailboxExportAccountForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // What an operator's listing reads: one account's exports, newest first.
        entity.HasIndex(export => new { export.MailboxAccountId, export.RequestedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailboxExportAccountTimelineIndexName)
            .IsDescending(false, true);

        // What the expiry pass reads, and the only path that reaches a row by its age. Partial, because the pass asks
        // only about archives that exist: an export that failed, was cancelled, or has already gone carries no expiry.
        entity.HasIndex(export => export.ExpiresAt)
            .HasDatabaseName(PersistenceConstraintNames.MailboxExportExpiryIndexName)
            .HasFilter("\"ExpiresAt\" IS NOT NULL");
    }
}

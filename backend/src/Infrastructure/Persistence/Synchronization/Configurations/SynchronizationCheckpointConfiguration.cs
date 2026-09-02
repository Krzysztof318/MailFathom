// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares how far synchronization has read one folder, as one row per folder that is erased with it.</summary>
internal sealed class SynchronizationCheckpointConfiguration : IEntityTypeConfiguration<SynchronizationCheckpointEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SynchronizationCheckpointEntity> entity)
    {
        entity.ToTable("synchronization_checkpoints");
        entity.HasKey(checkpoint => checkpoint.MailFolderId)
            .HasName(PersistenceConstraintNames.SynchronizationCheckpointPrimaryKeyConstraintName);
        entity.Property(checkpoint => checkpoint.MailFolderId).ValueGeneratedNever();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(checkpoint => checkpoint.ConcurrencyVersion).IsRowVersion();
        entity.HasOne(checkpoint => checkpoint.MailFolder)
            .WithOne(folder => folder.SynchronizationCheckpoint)
            .HasForeignKey<SynchronizationCheckpointEntity>(checkpoint => checkpoint.MailFolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

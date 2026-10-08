// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the folders an account's document maps, held as rows beside the account they belong to.</summary>
internal sealed class MailAccountFolderSettingsConfiguration : IEntityTypeConfiguration<MailAccountFolderSettingsEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAccountFolderSettingsEntity> entity)
    {
        entity.ToTable(MailAccountFolderSettingsEntity.TableName);

        // One row per alias of one account, which is the identity every folder decision is made about.
        entity.HasKey(folder => new { folder.MailAccountId, folder.Alias });

        entity.Property(folder => folder.Alias).HasMaxLength(MailAccountFolderSettingsEntity.MaximumAliasLength);

        entity.HasOne<MailAccountRecordEntity>()
            .WithMany()
            .HasForeignKey(folder => folder.MailAccountId)
            .HasConstraintName(PersistenceConstraintNames.MailAccountFolderSettingsAccountForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

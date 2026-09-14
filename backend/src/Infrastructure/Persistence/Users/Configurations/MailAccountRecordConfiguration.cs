// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the record one mail account is held as, apart from every user it is assigned to.</summary>
internal sealed class MailAccountRecordConfiguration : IEntityTypeConfiguration<MailAccountRecordEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAccountRecordEntity> entity)
    {
        entity.ToTable(MailAccountRecordEntity.TableName);
        entity.HasKey(account => account.Id);

        // Minted by whoever creates the account — the migration carrying an upgraded deployment's declarations, and the
        // administration after it — so the model never generates one behind a caller that meant to state it.
        entity.Property(account => account.Id).ValueGeneratedNever();

        entity.Property(account => account.EmailAddress).HasMaxLength(MailAccountRecordEntity.MaximumEmailAddressLength);
        entity.Property(account => account.NormalizedEmailAddress)
            .HasMaxLength(MailAccountRecordEntity.MaximumEmailAddressLength);

        // The one guarantee that a mailbox exists once in a deployment, and it is the index rather than a read before the
        // write: two writers claiming one address at once are separated here. Filtered, because an account the upgrade
        // could derive no address for is held without one until an administrator states it.
        entity.HasIndex(account => account.NormalizedEmailAddress)
            .IsUnique()
            .HasFilter("\"NormalizedEmailAddress\" IS NOT NULL")
            .HasDatabaseName(PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName);

        entity.Property(account => account.DisplayName)
            .HasMaxLength(MailAccountRecordEntity.MaximumDisplayNameLength)
            .IsRequired();

        entity.Property(account => account.Document).HasColumnType("jsonb").IsRequired();

        entity.Property(account => account.Version).IsConcurrencyToken();
    }
}

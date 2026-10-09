// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Maps the record of the default administrator.</summary>
internal sealed class DefaultAdministratorConfiguration : IEntityTypeConfiguration<DefaultAdministratorEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DefaultAdministratorEntity> entity)
    {
        entity.ToTable(
            DefaultAdministratorEntity.TableName,
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.DefaultAdministratorSingleRowCheckConstraintName,
                $"\"{nameof(DefaultAdministratorEntity.Id)}\" = {DefaultAdministratorEntity.SingleRowId}"));

        entity.HasKey(record => record.Id);
        entity.Property(record => record.Id).ValueGeneratedNever();

        // Cleared rather than cascaded, so erasing the administrator keeps the record that one was written and no later
        // start writes another.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

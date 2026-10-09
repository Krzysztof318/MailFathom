// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Grants.Configurations;

/// <summary>Declares the roles a deployment holds, unique by name.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<RoleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoleEntity> entity)
    {
        entity.ToTable(RoleEntity.TableName);
        entity.HasKey(role => role.Id);

        // Minted by the act that records the role, and fixed for the seeded ones, so it can be reported back.
        entity.Property(role => role.Id).ValueGeneratedNever();

        // Exact rather than case-folded, as a user's label is: nothing resolves a role by its name, and the uniqueness
        // exists so a listing never leaves an administrator choosing between two rows carrying one name.
        entity.Property(role => role.Name)
            .HasMaxLength(RoleEntity.MaximumNameLength)
            .IsRequired();
        entity.HasIndex(role => role.Name)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.RoleNameUniqueIndexName);
    }
}

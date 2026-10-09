// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Grants.Configurations;

/// <summary>Declares the permissions each role lists, one row per name.</summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermissionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RolePermissionEntity> entity)
    {
        entity.ToTable(RolePermissionEntity.TableName);

        // The pair is the key, so a role lists a name once however it was written.
        entity.HasKey(permission => new { permission.RoleId, permission.Permission });

        entity.Property(permission => permission.Permission)
            .HasMaxLength(RolePermissionEntity.MaximumPermissionLength);

        // A list is part of its role and leaves with it.
        entity.HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

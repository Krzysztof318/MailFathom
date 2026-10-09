// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Grants.Configurations;

/// <summary>Declares the role assignments a user's grant is computed from, and what the database refuses of one.</summary>
internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignmentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RoleAssignmentEntity> entity)
    {
        entity.ToTable(RoleAssignmentEntity.TableName);
        entity.HasKey(assignment => assignment.Id);

        entity.Property(assignment => assignment.Id).ValueGeneratedNever();

        // Restricted rather than cascaded: removing a role nobody has revoked would take a grant from everybody holding
        // it as the side effect of a deletion, so the store refuses that deletion and the key refuses it as well.
        entity.HasOne<RoleEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.RoleId)
            .HasConstraintName(PersistenceConstraintNames.RoleAssignmentRoleForeignKeyName)
            .OnDelete(DeleteBehavior.Restrict);

        // Every other side cascades, so no grant outlives what it was given to or what it reached. A group's deletion is
        // still refused while it is assigned, by the store under the group's lock; the cascade is what the organization
        // the group belongs to takes with it.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.PrincipalUserId)
            .HasConstraintName(PersistenceConstraintNames.RoleAssignmentPrincipalUserForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserGroupEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.PrincipalGroupId)
            .HasConstraintName(PersistenceConstraintNames.RoleAssignmentPrincipalGroupForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.ScopeOrganizationId)
            .HasConstraintName(PersistenceConstraintNames.RoleAssignmentScopeOrganizationForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(assignment => assignment.ScopeUserId)
            .HasConstraintName(PersistenceConstraintNames.RoleAssignmentScopeUserForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(assignment => new
        {
            assignment.RoleId,
            assignment.PrincipalUserId,
            assignment.PrincipalGroupId,
            assignment.ScopeOrganizationId,
            assignment.ScopeUserId,
        })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(PersistenceConstraintNames.RoleAssignmentUniqueIndexName);

        entity.ToTable(table =>
        {
            table.HasCheckConstraint(
                PersistenceConstraintNames.RoleAssignmentPrincipalCheckConstraintName,
                $"num_nonnulls(\"{nameof(RoleAssignmentEntity.PrincipalUserId)}\", \"{nameof(RoleAssignmentEntity.PrincipalGroupId)}\") = 1");
            table.HasCheckConstraint(
                PersistenceConstraintNames.RoleAssignmentScopeCheckConstraintName,
                $"num_nonnulls(\"{nameof(RoleAssignmentEntity.ScopeOrganizationId)}\", \"{nameof(RoleAssignmentEntity.ScopeUserId)}\") <= 1");
        });
    }
}

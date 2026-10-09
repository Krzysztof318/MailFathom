// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Grants.Configurations;

/// <summary>Declares the groups a deployment holds, unique by name, each in no organization or in one.</summary>
internal sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroupEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserGroupEntity> entity)
    {
        entity.ToTable(UserGroupEntity.TableName);
        entity.HasKey(group => group.Id);

        entity.Property(group => group.Id).ValueGeneratedNever();

        entity.Property(group => group.Name)
            .HasMaxLength(UserGroupEntity.MaximumNameLength)
            .IsRequired();
        entity.HasIndex(group => group.Name)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.UserGroupNameUniqueIndexName);

        // Cascaded rather than restricted, unlike a user's organization: an organization is removed only once nobody
        // belongs to it, so a group in it has no members left, and removing the organization takes every membership and
        // assignment inside it with it rather than leaving an empty group nobody's scope reaches.
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(group => group.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

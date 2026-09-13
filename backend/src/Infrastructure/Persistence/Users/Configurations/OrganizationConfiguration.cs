// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the organizations users are grouped into, and the index a login's short name is resolved by.</summary>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<OrganizationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrganizationEntity> entity)
    {
        entity.ToTable(OrganizationEntity.TableName);
        entity.HasKey(organization => organization.Id);

        // Minted by the administrative act that records the organization, so it can report it back.
        entity.Property(organization => organization.Id).ValueGeneratedNever();

        entity.Property(organization => organization.DisplayName)
            .HasMaxLength(OrganizationEntity.MaximumDisplayNameLength)
            .IsRequired();

        // Unique across the deployment, because it is half of a login: two organizations under one short name would leave
        // which company a sign-in reached decided by the order the database returned them. It is also what a sign-in
        // resolves the organization by, inside the same statement as the credential.
        entity.Property(organization => organization.ShortName)
            .HasMaxLength(OrganizationEntity.MaximumShortNameLength)
            .IsRequired();
        entity.HasIndex(organization => organization.ShortName)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.OrganizationShortNameUniqueIndexName);
    }
}

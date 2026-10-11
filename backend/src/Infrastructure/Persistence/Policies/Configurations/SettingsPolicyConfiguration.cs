// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Policies.Configurations;

/// <summary>Maps the settings policy each scope holds.</summary>
internal sealed class SettingsPolicyConfiguration : IEntityTypeConfiguration<SettingsPolicyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SettingsPolicyEntity> entity)
    {
        entity.ToTable(SettingsPolicyEntity.TableName);
        entity.HasKey(policy => policy.Id);

        // Minted by the write that first stores a scope's policy, so the model never mints one behind it.
        entity.Property(policy => policy.Id).ValueGeneratedNever();

        // One policy per scope, which is the guarantee two administrators storing a scope's first policy at once are
        // held to: the second insert violates this rather than leaving two rows and no way to say which one governs.
        // Nulls are not distinct, so the deployment's own row — the one naming no organization — is as unique as an
        // organization's.
        entity.HasIndex(policy => policy.OrganizationId)
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(PersistenceConstraintNames.SettingsPolicyScopeUniqueIndexName);

        // Cascaded, as the organization's groups and the roles given at it are, and unlike its members and its mail
        // accounts, which restrict the deletion: those are something a deletion must not take with it, while a policy
        // is nothing but what that organization said about them, and an organization is deleted only once it has
        // neither.
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(policy => policy.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(PersistenceConstraintNames.SettingsPolicyOrganizationForeignKeyName);

        // A document rather than a schema, because what it holds follows the shape of the records it governs, and a
        // property either of those gains must cost this table nothing.
        entity.Property(policy => policy.Document).HasColumnType("jsonb").IsRequired();

        // The policy's own version rather than PostgreSQL's row version, for the reason the deployment's document
        // carries one: a writer has to state which version it read, be refused by number, and be told the version it
        // was refused against.
        entity.Property(policy => policy.Version).IsConcurrencyToken();
    }
}

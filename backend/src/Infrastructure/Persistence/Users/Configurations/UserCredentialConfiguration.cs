// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the credentials one user is admitted by, and the index a request resolves one by.</summary>
/// <remarks>
/// The table hangs off the user row and nothing else. Its foreign key cascades, so erasing a user takes their means
/// of being reached with them rather than leaving credentials that resolve nobody — which is the same reason every other
/// user-scoped table declares one.
/// </remarks>
internal sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredentialEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserCredentialEntity> entity)
    {
        entity.ToTable(UserCredentialEntity.TableName);
        entity.HasKey(credential => credential.Id);

        // Provisioned rather than generated on insert, for the reason the user row's identifier is: the administrative
        // act that creates a credential mints the identifier so it can report it back, and the model never mints one
        // behind a caller that meant to state it.
        entity.Property(credential => credential.Id).ValueGeneratedNever();

        entity.Property(credential => credential.Method)
            .HasMaxLength(UserCredentialEntity.MaximumMethodLength)
            .IsRequired();

        entity.Property(credential => credential.Lookup)
            .HasMaxLength(UserCredentialLookup.MaximumLength)
            .IsRequired();

        // Unique within the method and the organization rather than within one user, because a request presents a login
        // and nothing else: a value carried by two rows would leave which user it authenticates decided by the order the
        // database returned them. It is scoped to the method because the four vocabularies are unrelated, and to the
        // organization because a password's username is unique only within one. Nulls are not distinct, so the
        // credentials scoped to no organization — every one of the other three methods, and a password of somebody in
        // none — are still unique among themselves.
        entity.HasIndex(credential => new { credential.Method, credential.OrganizationId, credential.Lookup })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(PersistenceConstraintNames.UserCredentialLookupUniqueIndexName);

        // Only a password is scoped to an organization, so a row of another method carrying one is a write the store
        // must never make rather than a state the index should have to reason about.
        entity.ToTable(table => table.HasCheckConstraint(
            PersistenceConstraintNames.UserCredentialOrganizationScopesPasswordCheckConstraintName,
            $"\"{nameof(UserCredentialEntity.OrganizationId)}\" IS NULL OR \"{nameof(UserCredentialEntity.Method)}\" = 'password'"));

        // Restricted rather than cascaded: an organization is deleted only once nobody belongs to it, and a credential
        // still naming one is a member's credential that the deletion must not take with it.
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(credential => credential.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.Property(credential => credential.Material)
            .HasMaxLength(UserCredentialEntity.MaximumMaterialLength);

        // A PostgreSQL text array rather than a joined string, so a grant is read back as the set it is and a value
        // carrying the separator cannot be composed into two permissions. Nothing queries by an element today, which is
        // why no index sits on it: the grant is read once the row the lookup resolved is already in hand.
        entity.Property(credential => credential.Permissions).IsRequired();

        // Every listing an administrator reads is one user's, and so is every write, so the index that answers them is
        // the user's rather than the primary key's.
        entity.HasIndex(credential => new { credential.UserId, credential.CreatedAt })
            .HasDatabaseName(PersistenceConstraintNames.UserCredentialUserIndexName);

        entity.Property(credential => credential.Version).IsConcurrencyToken();

        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(credential => credential.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

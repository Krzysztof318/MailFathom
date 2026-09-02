// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Owners.Configurations;

/// <summary>Declares the credentials one owner is admitted by, and the index a request resolves one by.</summary>
/// <remarks>
/// The table hangs off the owner row and nothing else. Its foreign key cascades, so erasing an owner takes their means
/// of being reached with them rather than leaving credentials that resolve nobody — which is the same reason every other
/// owner-scoped table declares one.
/// </remarks>
internal sealed class OwnerCredentialConfiguration : IEntityTypeConfiguration<OwnerCredentialEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OwnerCredentialEntity> entity)
    {
        entity.ToTable("owner_credentials");
        entity.HasKey(credential => credential.Id);

        // Provisioned rather than generated on insert, for the reason the owner row's identifier is: the administrative
        // act that creates a credential mints the identifier so it can report it back, and the model never mints one
        // behind a caller that meant to state it.
        entity.Property(credential => credential.Id).ValueGeneratedNever();

        entity.Property(credential => credential.Method)
            .HasMaxLength(OwnerCredentialEntity.MaximumMethodLength)
            .IsRequired();

        entity.Property(credential => credential.Lookup)
            .HasMaxLength(OwnerCredentialLookup.MaximumLength)
            .IsRequired();

        // Unique within the method and across the deployment rather than within one owner, because a request presents a
        // lookup and nothing else: a value carried by two rows would leave which owner it authenticates decided by the
        // order the database returned them. It is scoped to the method because the four vocabularies are unrelated —
        // a username and a digest that happened to spell the same thing are two different credentials. The name is
        // stated because a refusal an operator reads is worth naming the index it came from.
        entity.HasIndex(credential => new { credential.Method, credential.Lookup })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.OwnerCredentialLookupUniqueIndexName);

        entity.Property(credential => credential.Material)
            .HasMaxLength(OwnerCredentialEntity.MaximumMaterialLength);

        // A PostgreSQL text array rather than a joined string, so a grant is read back as the set it is and a value
        // carrying the separator cannot be composed into two permissions. Nothing queries by an element today, which is
        // why no index sits on it: the grant is read once the row the lookup resolved is already in hand.
        entity.Property(credential => credential.Permissions).IsRequired();

        // Every listing an administrator reads is one owner's, and so is every write, so the index that answers them is
        // the owner's rather than the primary key's.
        entity.HasIndex(credential => new { credential.OwnerId, credential.CreatedAt })
            .HasDatabaseName(PersistenceConstraintNames.OwnerCredentialOwnerIndexName);

        entity.Property(credential => credential.Version).IsConcurrencyToken();

        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(credential => credential.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

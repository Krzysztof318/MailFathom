// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Owners.Configurations;

/// <summary>Declares the credentials one owner signs in with, and the index a request resolves one by.</summary>
/// <remarks>
/// The table hangs off the owner row and nothing else. Its foreign key cascades, so erasing an owner takes their means
/// of signing in with them rather than leaving credentials that resolve nobody — which is the same reason every other
/// owner-scoped table declares one.
/// </remarks>
internal sealed class OwnerPasswordCredentialConfiguration : IEntityTypeConfiguration<OwnerPasswordCredentialEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OwnerPasswordCredentialEntity> entity)
    {
        entity.ToTable("owner_password_credentials");
        entity.HasKey(credential => credential.Id);

        // Provisioned rather than generated on insert, for the reason the owner row's identifier is: the administrative
        // act that creates a credential mints the identifier so it can report it back, and the model never mints one
        // behind a caller that meant to state it.
        entity.Property(credential => credential.Id).ValueGeneratedNever();

        entity.Property(credential => credential.Username)
            .HasMaxLength(OwnerCredentialUsername.MaximumLength)
            .IsRequired();

        // Unique across the deployment rather than within one owner, because a request presents a username and nothing
        // else: a name carried by two rows would leave which owner it authenticates decided by the order the database
        // returned them. The name is stated because a refusal an operator reads is worth naming the index it came from.
        entity.HasIndex(credential => credential.Username)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.OwnerPasswordCredentialUsernameUniqueIndexName);

        entity.Property(credential => credential.PasswordHash)
            .HasMaxLength(OwnerPasswordCredentialEntity.MaximumPasswordHashLength)
            .IsRequired();

        // Every listing an administrator reads is one owner's, and so is every write, so the index that answers them is
        // the owner's rather than the primary key's.
        entity.HasIndex(credential => new { credential.OwnerId, credential.CreatedAt })
            .HasDatabaseName(PersistenceConstraintNames.OwnerPasswordCredentialOwnerIndexName);

        entity.Property(credential => credential.Version).IsConcurrencyToken();

        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(credential => credential.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

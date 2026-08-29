// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Secrets.Configurations;

/// <summary>Declares the sealed material one database secret reference identifies.</summary>
internal sealed class StoredSecretConfiguration : IEntityTypeConfiguration<StoredSecretEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredSecretEntity> entity)
    {
        entity.ToTable(
            "stored_secrets",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.StoredSecretMaterialLengthCheckConstraintName,
                $"octet_length(\"SealedMaterial\") BETWEEN {StoredSecretEntity.MinimumSealedMaterialByteCount} AND {StoredSecretEntity.MaximumSealedMaterialByteCount}"));
        entity.HasKey(secret => secret.Id);
        entity.Property(secret => secret.Id).ValueGeneratedNever();
        entity.Property(secret => secret.Name)
            .HasMaxLength(StoredSecretEntity.MaximumNameLength)
            .IsRequired();
        entity.Property(secret => secret.SealedMaterial).HasColumnType("bytea").IsRequired();
        entity.Property(secret => secret.DataEncryptionKeyId)
            .HasMaxLength(StoredSecretEntity.MaximumKeyIdLength)
            .IsRequired();

        entity.HasOne(secret => secret.Owner)
            .WithMany()
            .HasForeignKey(secret => secret.OwnerId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(PersistenceConstraintNames.StoredSecretOwnerForeignKeyName);

        entity.HasIndex(secret => new { secret.OwnerId, secret.Name })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.StoredSecretOwnerNameUniqueIndexName);
        entity.HasIndex(secret => secret.DataEncryptionKeyId)
            .HasDatabaseName(PersistenceConstraintNames.StoredSecretKeyIndexName);
    }
}

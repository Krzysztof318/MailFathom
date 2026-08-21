// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Embeddings.Configurations;

/// <summary>Declares the vector spaces this deployment has embedded into.</summary>
/// <remarks>
/// <para>
/// The identity columns are fixed at insertion and the fingerprint over them carries a unique index, so activating a
/// declaration whose geometry already exists resolves to the existing row rather than inserting a second one that
/// would be re-embedded from scratch. Nothing in the schema stops an update of an identity column; what the schema
/// owns is the consequence, since a changed identity would collide with its own fingerprint or leave one describing
/// nothing.
/// </para>
/// <para>
/// The alternate key over the identifier and the dimension exists for one reader: <see cref="EmailEmbeddingEntity" />
/// points a composite foreign key at it, which is the only way a check constraint — which sees one row — can be made
/// to enforce a width this table declares.
/// </para>
/// </remarks>
internal sealed class EmbeddingProfileConfiguration : IEntityTypeConfiguration<EmbeddingProfileEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmbeddingProfileEntity> entity)
    {
        entity.ToTable("embedding_profiles");
        entity.HasKey(profile => profile.Id);
        entity.Property(profile => profile.Id).ValueGeneratedNever();

        entity.Property(profile => profile.Provider)
            .HasMaxLength(EmbeddingProfileIdentity.MaximumProviderLength)
            .IsRequired();
        entity.Property(profile => profile.ModelIdentifier)
            .HasMaxLength(EmbeddingProfileIdentity.MaximumModelIdentifierLength)
            .IsRequired();
        entity.Property(profile => profile.ModelVersion)
            .HasMaxLength(EmbeddingProfileIdentity.MaximumModelVersionLength);
        entity.Property(profile => profile.PassageInstruction)
            .HasMaxLength(EmbeddingInputPreparation.MaximumPassageInstructionLength);

        // Stored as text for the reason every other enum column here is: the value stays readable in an ad-hoc audit
        // query and survives any later reordering of the enum.
        entity.Property(profile => profile.DistanceMetric).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(profile => profile.LifecycleState).HasConversion<string>().HasMaxLength(64).IsRequired();

        // Fixed length because a SHA-256 digest has one, and text rather than `bytea` because activation compares
        // this value and an operator reading a profile reads it.
        entity.Property(profile => profile.IdentityFingerprint)
            .HasMaxLength(EmbeddingProfileFingerprint.Length)
            .IsFixedLength()
            .IsRequired();

        entity.HasIndex(profile => profile.IdentityFingerprint)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.EmbeddingProfileFingerprintUniqueIndexName);

        // Unique over the state itself and partial to the two states that admit one row each, which is how one
        // index expresses both halves of the invariant: at most one generation being built, and at most one being
        // read. The literals are the enum member names because the column stores those names.
        entity.HasIndex(profile => profile.LifecycleState)
            .IsUnique()
            .HasFilter($"\"LifecycleState\" IN ('{nameof(EmbeddingProfileLifecycleState.Building)}', '{nameof(EmbeddingProfileLifecycleState.Active)}')")
            .HasDatabaseName(PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName);

        entity.HasAlternateKey(profile => new { profile.Id, profile.Dimension })
            .HasName(PersistenceConstraintNames.EmbeddingProfileDimensionAlternateKeyName);
    }
}

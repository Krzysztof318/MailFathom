// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Embeddings.Configurations;

/// <summary>Declares the vector column and the two constraints that keep a stored vector meaning what its profile says.</summary>
/// <remarks>
/// <para>
/// The column is pgvector's dimensionless <c>vector</c>, so two profiles of different widths coexist in one table and
/// each is served by an expression index created when it is activated. The width is enforced instead by a pair: a
/// composite foreign key onto the profile's own dimension, which refuses a width the profile never declared, and a
/// check constraint comparing that column against the stored vector's actual length. Neither half works alone —
/// PostgreSQL evaluates a check against one row, so without the foreign key the check would only prove a vector
/// agrees with a number beside it.
/// </para>
/// <para>
/// The chunk cascades and the profile does not. Deleting a message must reach every vector derived from it, which is
/// what the cascade makes structural rather than a rule somebody has to remember; a profile, by contrast, is what a
/// stored vector's attribution points at, so the schema refuses to remove one while a vector still names it.
/// </para>
/// </remarks>
internal sealed class EmailEmbeddingConfiguration : IEntityTypeConfiguration<EmailEmbeddingEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailEmbeddingEntity> entity)
    {
        entity.ToTable(
            "email_embeddings",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.EmailEmbeddingDimensionCheckConstraintName,
                $"vector_dims(\"{nameof(EmailEmbeddingEntity.Embedding)}\") = \"{nameof(EmailEmbeddingEntity.Dimension)}\""));

        // The chunk and the profile together, because that pair is what a vector is: re-embedding a passage under
        // the profile already serving it replaces the row rather than adding one. Named so an idempotent upsert has
        // a constraint to conflict on.
        entity.HasKey(embedding => new { embedding.EmailChunkId, embedding.EmbeddingProfileId })
            .HasName(PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName);

        entity.Property(embedding => embedding.Embedding).HasColumnType("vector").IsRequired();

        entity.HasOne(embedding => embedding.EmailChunk)
            .WithMany(chunk => chunk.Embeddings)
            .HasForeignKey(embedding => embedding.EmailChunkId)
            .OnDelete(DeleteBehavior.Cascade);

        // Declared rather than left to the foreign key's own convention, because a superseded generation is deleted
        // in bounded batches read by profile, and that read would otherwise scan every vector in the table.
        entity.HasIndex(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasDatabaseName(PersistenceConstraintNames.EmailEmbeddingProfileIndexName);

        entity.HasOne(embedding => embedding.EmbeddingProfile)
            .WithMany(profile => profile.Embeddings)
            .HasForeignKey(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasPrincipalKey(profile => new { profile.Id, profile.Dimension })
            .HasConstraintName(PersistenceConstraintNames.EmailEmbeddingProfileForeignKeyName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

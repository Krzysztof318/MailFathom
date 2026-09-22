// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Agent.Configurations;

internal sealed class AgentConversationEmbeddingConfiguration : IEntityTypeConfiguration<AgentConversationEmbeddingEntity>
{
    public void Configure(EntityTypeBuilder<AgentConversationEmbeddingEntity> entity)
    {
        entity.ToTable(
            AgentConversationEmbeddingEntity.TableName,
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.AgentConversationEmbeddingDimensionCheckConstraintName,
                $"vector_dims(\"{AgentConversationEmbeddingEntity.EmbeddingColumnName}\") = \"{AgentConversationEmbeddingEntity.DimensionColumnName}\""));

        entity.HasKey(embedding => new { embedding.ConversationId, embedding.Sequence, embedding.EmbeddingProfileId })
            .HasName(PersistenceConstraintNames.AgentConversationEmbeddingPrimaryKeyConstraintName);
        entity.Property(embedding => embedding.ConversationId)
            .HasColumnName(AgentConversationEmbeddingEntity.ConversationIdColumnName);
        entity.Property(embedding => embedding.Sequence)
            .HasColumnName(AgentConversationEmbeddingEntity.SequenceColumnName)
            .ValueGeneratedNever();
        entity.Property(embedding => embedding.EmbeddingProfileId)
            .HasColumnName(AgentConversationEmbeddingEntity.EmbeddingProfileIdColumnName);
        entity.Property(embedding => embedding.Dimension)
            .HasColumnName(AgentConversationEmbeddingEntity.DimensionColumnName);
        entity.Property(embedding => embedding.Embedding)
            .HasColumnName(AgentConversationEmbeddingEntity.EmbeddingColumnName)
            .HasColumnType("vector")
            .IsRequired();
        entity.Property(embedding => embedding.GeneratedAt)
            .HasColumnName(AgentConversationEmbeddingEntity.GeneratedAtColumnName);

        // The entry rather than the conversation, so the vector goes with the words it was placed from by the cascade
        // every entry already goes by — a conversation deleted, or its person erased, takes its vectors along.
        entity.HasOne<AgentConversationEntryEntity>()
            .WithMany()
            .HasForeignKey(embedding => new { embedding.ConversationId, embedding.Sequence })
            .HasConstraintName(PersistenceConstraintNames.AgentConversationEmbeddingEntryForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // Declared for the reason the mail vectors' index is: a superseded space's vectors are removed by profile.
        entity.HasIndex(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasDatabaseName(PersistenceConstraintNames.AgentConversationEmbeddingProfileIndexName);

        entity.HasOne<EmbeddingProfileEntity>()
            .WithMany()
            .HasForeignKey(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
            .HasPrincipalKey(profile => new { profile.Id, profile.Dimension })
            .HasConstraintName(PersistenceConstraintNames.AgentConversationEmbeddingProfileForeignKeyName)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

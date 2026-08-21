// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Embeddings.Configurations;

/// <summary>Declares what one budget period of embedding generation cost this deployment.</summary>
/// <remarks>
/// One row per budget period, keyed by the instant that period began. Nothing hangs off it and nothing cascades into
/// it: what it records is a cost this deployment incurred, which stays true after every vector that cost paid for has
/// been superseded and removed. The column names are the entity's own constants because the one write is a composed
/// upsert, so the statement and this mapping name the same things by construction.
/// </remarks>
internal sealed class EmbeddingSpendPeriodConfiguration : IEntityTypeConfiguration<EmbeddingSpendPeriodEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmbeddingSpendPeriodEntity> entity)
    {
        entity.ToTable(EmbeddingSpendPeriodEntity.TableName);
        entity.HasKey(period => period.PeriodStartsAt);
        entity.Property(period => period.PeriodStartsAt)
            .HasColumnName(EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.ConsumedInputCharacterCount)
            .HasColumnName(EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName);
    }
}

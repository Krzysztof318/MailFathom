// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Embeddings.Configurations;

/// <summary>Declares what one budget period of embedding generation cost each owner this deployment serves.</summary>
/// <remarks>
/// One row per budget period and owner, keyed by the instant that period began and by the owner it was spent for, in
/// that order — so the same key answers what one owner spent and, as a range over its leading column, what the
/// deployment spent. Nothing hangs off it and nothing cascades into it, not even from the owner record: what it
/// records is a cost that was incurred, which stays true after every vector that cost paid for has been superseded and
/// removed, and after the owner it was incurred for has been erased. The column names are the entity's own constants
/// because the one write is a composed upsert, so the statement and this mapping name the same things by construction.
/// </remarks>
internal sealed class EmbeddingSpendPeriodConfiguration : IEntityTypeConfiguration<EmbeddingSpendPeriodEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmbeddingSpendPeriodEntity> entity)
    {
        entity.ToTable(EmbeddingSpendPeriodEntity.TableName);
        entity.HasKey(period => new { period.PeriodStartsAt, period.OwnerId });
        entity.Property(period => period.PeriodStartsAt)
            .HasColumnName(EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.OwnerId)
            .HasColumnName(EmbeddingSpendPeriodEntity.OwnerIdColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.ConsumedInputCharacterCount)
            .HasColumnName(EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Answering.Configurations;

/// <summary>Declares what each answering period of this deployment admitted and what it cost.</summary>
/// <remarks>
/// Keyed by the period's start alone, because both ceilings it holds are the deployment's and neither has a per-user
/// form. Nothing hangs off it and nothing cascades into it: what it records is a cost that was incurred, which stays
/// true after every question it paid for has been answered and forgotten. The column names are the entity's own
/// constants because both writes are composed statements, so the statements and this mapping name the same things by
/// construction.
/// </remarks>
internal sealed class MailAnsweringSpendPeriodConfiguration : IEntityTypeConfiguration<MailAnsweringSpendPeriodEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAnsweringSpendPeriodEntity> entity)
    {
        entity.ToTable(MailAnsweringSpendPeriodEntity.TableName);
        entity.HasKey(period => period.PeriodStartsAt);
        entity.Property(period => period.PeriodStartsAt)
            .HasColumnName(MailAnsweringSpendPeriodEntity.PeriodStartsAtColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.AdmittedRunCount)
            .HasColumnName(MailAnsweringSpendPeriodEntity.AdmittedRunCountColumnName);
        entity.Property(period => period.ConsumedTokenCount)
            .HasColumnName(MailAnsweringSpendPeriodEntity.ConsumedTokenCountColumnName);
    }
}

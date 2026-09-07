// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares what one budget period of attachment reading cost each user this deployment serves, step by step.</summary>
/// <remarks>
/// One row per budget period, user, and step, keyed in that order — so the same key answers what one user consumed on
/// one step, as a range over its first two columns what that user consumed altogether, and as a range over its leading
/// column what the deployment consumed. Nothing hangs off it and nothing cascades into it, not even from the user
/// record: what it records is a cost that was incurred, which stays true after the readings it paid for have been
/// discarded and after the user it was incurred for has been erased. The step is stored as its name for the reason
/// every other enumeration in this schema is — a row read outside this process says what it means — and the column
/// names are the entity's own constants, because the one write is a composed upsert and the statement and this mapping
/// must name the same things by construction.
/// </remarks>
internal sealed class AttachmentDerivationSpendPeriodConfiguration
    : IEntityTypeConfiguration<AttachmentDerivationSpendPeriodEntity>
{
    /// <summary>The characters a step name occupies, which is far past the longest member and short enough to index.</summary>
    private const int StepNameLength = 32;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AttachmentDerivationSpendPeriodEntity> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.ToTable(AttachmentDerivationSpendPeriodEntity.TableName);
        entity.HasKey(period => new { period.PeriodStartsAt, period.UserId, period.Step });
        entity.Property(period => period.PeriodStartsAt)
            .HasColumnName(AttachmentDerivationSpendPeriodEntity.PeriodStartsAtColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.UserId)
            .HasColumnName(AttachmentDerivationSpendPeriodEntity.UserIdColumnName)
            .ValueGeneratedNever();
        entity.Property(period => period.Step)
            .HasColumnName(AttachmentDerivationSpendPeriodEntity.StepColumnName)
            .HasConversion<string>()
            .HasMaxLength(StepNameLength)
            .ValueGeneratedNever();
        entity.Property(period => period.ConsumedUnitCount)
            .HasColumnName(AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares how far each named backfill has walked, as one row per pass.</summary>
/// <remarks>
/// Keyed by the pass's own name rather than by an account, because a backfill walks the deployment's stored mail once
/// rather than one mailbox at a time. Nothing hangs off the row and nothing cascades into it: what it records is where
/// a walk stopped, which stays true whatever becomes of the mail it had reached.
/// </remarks>
internal sealed class BackfillPositionConfiguration : IEntityTypeConfiguration<BackfillPositionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<BackfillPositionEntity> entity)
    {
        entity.ToTable("backfill_positions");
        entity.HasKey(position => position.Name);
        entity.Property(position => position.Name).HasMaxLength(BackfillPositionEntity.MaximumNameLength);
        entity.Property(position => position.SensitiveContentStamp)
            .HasMaxLength(SensitiveContentDerivationStamp.Length)
            .IsFixedLength();
    }
}

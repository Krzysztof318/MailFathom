// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the maintained figure of what one owner's stored mail content holds.</summary>
/// <remarks>
/// Keyed by the owner alone, because there is one figure per person and it is read by that key before every folder run.
/// It cascades from the owner record, so erasing an owner takes the figure with the mail it described. The column names
/// are the entity's own constants because both writes are composed statements, so the statements and this mapping name
/// the same things by construction.
/// </remarks>
internal sealed class OwnerStoredContentConfiguration : IEntityTypeConfiguration<OwnerStoredContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OwnerStoredContentEntity> entity)
    {
        entity.ToTable(OwnerStoredContentEntity.TableName);
        entity.HasKey(total => total.OwnerId);
        entity.Property(total => total.OwnerId)
            .HasColumnName(OwnerStoredContentEntity.OwnerIdColumnName)
            .ValueGeneratedNever();
        entity.Property(total => total.StoredContentByteCount)
            .HasColumnName(OwnerStoredContentEntity.StoredContentByteCountColumnName);
        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(total => total.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

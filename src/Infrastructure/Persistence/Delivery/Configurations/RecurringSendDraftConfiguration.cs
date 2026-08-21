// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the draft every occurrence of one recurring send is composed from.</summary>
/// <remarks>
/// The same one-to-one arrangement the outgoing content table uses, and for the same reason: the bytes stay out of
/// every query that reads what repeats. What differs is that nothing transmits these bytes — each occasion composes
/// a message of its own from them — and the cascade is the erasure obligation, so a draft cannot outlive the
/// declaration that says who it was for.
/// </remarks>
internal sealed class RecurringSendDraftConfiguration : IEntityTypeConfiguration<RecurringSendDraftEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecurringSendDraftEntity> entity)
    {
        entity.ToTable("recurring_send_drafts");
        entity.HasKey(draft => draft.RecurringSendId);
        entity.Property(draft => draft.RecurringSendId).ValueGeneratedNever();
        entity.Property(draft => draft.DraftMime).IsRequired();
        entity.Property(draft => draft.Sha256Hash).HasMaxLength(32).IsRequired();

        entity.HasOne(draft => draft.RecurringSend)
            .WithOne(declaration => declaration.Draft)
            .HasForeignKey<RecurringSendDraftEntity>(draft => draft.RecurringSendId)
            .HasConstraintName(PersistenceConstraintNames.RecurringSendDraftForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
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
        entity.ToTable(
            "recurring_send_drafts",
            table => table.HasCheckConstraint(
                "ck_recurring_send_drafts_backend_payload",
                """
                ("Backend" = 'Database' AND "DraftMime" IS NOT NULL AND "ObjectLocator" IS NULL AND "ObjectVerifiedAt" IS NULL)
                OR ("Backend" = 'ObjectStorage' AND "ObjectLocator" IS NOT NULL
                    AND ("DraftMime" IS NULL OR "ObjectVerifiedAt" IS NOT NULL))
                """));
        entity.HasKey(draft => draft.RecurringSendId);
        entity.Property(draft => draft.RecurringSendId).ValueGeneratedNever();
        entity.Property(draft => draft.Backend)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(ContentStorageBackend.Database);
        entity.Property(draft => draft.ObjectLocator).HasMaxLength(1024);
        entity.Property(draft => draft.Sha256Hash).HasMaxLength(32).IsRequired();

        entity.HasOne(draft => draft.RecurringSend)
            .WithOne(declaration => declaration.Draft)
            .HasForeignKey<RecurringSendDraftEntity>(draft => draft.RecurringSendId)
            .HasConstraintName(PersistenceConstraintNames.RecurringSendDraftForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The index both readers of the object backend meet, and unique because a key is minted by the write that
        // produced it: the readiness census asks whether any row here names an object at all, and the sweep for objects
        // nothing points at asks whether any row names each of a listed page of keys. Filtered to the object backend so
        // a deployment that configured no endpoint answers either from an empty index rather than by scanning the table.
        entity.HasIndex(draft => draft.ObjectLocator)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.RecurringSendDraftObjectLocatorUniqueIndexName)
            .HasFilter($"\"Backend\" = '{nameof(ContentStorageBackend.ObjectStorage)}'");
    }
}

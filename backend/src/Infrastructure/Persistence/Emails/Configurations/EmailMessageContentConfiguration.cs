// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the raw MIME one stored email was read as, in a table of its own so no mailbox query loads it.</summary>
internal sealed class EmailMessageContentConfiguration : IEntityTypeConfiguration<EmailMessageContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailMessageContentEntity> entity)
    {
        entity.ToTable(
            "email_message_contents",
            table => table.HasCheckConstraint(
                "ck_email_message_contents_backend_payload",
                """
                ("Backend" = 'Database' AND "RawMime" IS NOT NULL AND "ObjectLocator" IS NULL)
                OR ("Backend" = 'ObjectStorage' AND "ObjectLocator" IS NOT NULL AND "RawMime" IS NULL)
                """));
        entity.HasKey(content => content.StoredEmailId);
        entity.Property(content => content.StoredEmailId).ValueGeneratedNever();
        entity.Property(content => content.RawMime).HasColumnType("bytea");
        entity.Property(content => content.Backend)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(ContentStorageBackend.Database);
        entity.Property(content => content.ObjectLocator).HasMaxLength(1024);
        entity.Property(content => content.Sha256Hash).HasColumnType("bytea").IsRequired();
        entity.HasOne(content => content.StoredEmail)
            .WithOne(email => email.Content)
            .HasForeignKey<EmailMessageContentEntity>(content => content.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        // The index both readers of the object backend meet, and unique because a key is minted by the write that
        // produced it: the readiness census asks whether any row here names an object at all, and the sweep for objects
        // nothing points at asks whether any row names each of a listed page of keys. Filtered to the object backend so
        // a deployment that configured no endpoint answers either from an empty index rather than by scanning the table.
        entity.HasIndex(content => content.ObjectLocator)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.EmailMessageContentObjectLocatorUniqueIndexName)
            .HasFilter($"\"Backend\" = '{nameof(ContentStorageBackend.ObjectStorage)}'");
    }
}

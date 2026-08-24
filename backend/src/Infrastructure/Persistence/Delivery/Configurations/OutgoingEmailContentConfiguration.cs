// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the raw MIME one outgoing email is transmitted as, stored once and read back per attempt.</summary>
/// <remarks>
/// <para>
/// A one-to-one table whose primary key is also its foreign key, which is the arrangement the incoming content table
/// uses and for the same reason: keeping the large binary value out of the record means listing what is queued never
/// loads a single message's bytes. PostgreSQL stores an oversized <c>bytea</c> out of line automatically.
/// </para>
/// <para>
/// The message is written once and read back rather than recomposed, because a message rebuilt between attempts
/// carries a different <c>Message-ID</c> and would thread as a second message in every recipient's client. The
/// cascade is the erasure obligation: deleting the record destroys the message it points at, so an outgoing email
/// cannot outlive the record that says who it was for.
/// </para>
/// </remarks>
internal sealed class OutgoingEmailContentConfiguration : IEntityTypeConfiguration<OutgoingEmailContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutgoingEmailContentEntity> entity)
    {
        entity.ToTable(
            "outgoing_email_contents",
            table => table.HasCheckConstraint(
                "ck_outgoing_email_contents_backend_payload",
                """
                ("Backend" = 'Database' AND "RawMime" IS NOT NULL AND "ObjectLocator" IS NULL)
                OR ("Backend" = 'ObjectStorage' AND "ObjectLocator" IS NOT NULL)
                """));
        entity.HasKey(content => content.OutgoingEmailId);
        entity.Property(content => content.OutgoingEmailId).ValueGeneratedNever();
        entity.Property(content => content.Backend)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(ContentStorageBackend.Database);
        entity.Property(content => content.ObjectLocator).HasMaxLength(1024);
        entity.Property(content => content.Sha256Hash).HasMaxLength(32).IsRequired();

        entity.HasOne(content => content.OutgoingEmail)
            .WithOne(message => message.Content)
            .HasForeignKey<OutgoingEmailContentEntity>(content => content.OutgoingEmailId)
            .HasConstraintName(PersistenceConstraintNames.OutgoingEmailContentForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

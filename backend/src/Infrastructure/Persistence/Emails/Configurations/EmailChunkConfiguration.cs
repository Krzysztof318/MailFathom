// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the passages one email's text is cut into for retrieval.</summary>
/// <remarks>
/// Many rows per email, keyed by a surrogate identifier rather than by the email and the ordinal together, because a
/// vector row hangs on one chunk and a composite key would put a re-cut message's ordinals into every table that
/// references it. The pair is a unique index instead, which is what a reader of one message's passages orders by and
/// what stops a re-cut from writing an ordinal twice.
/// </remarks>
internal sealed class EmailChunkConfiguration : IEntityTypeConfiguration<EmailChunkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailChunkEntity> entity)
    {
        entity.ToTable("email_chunks");
        entity.HasKey(chunk => chunk.Id);
        entity.Property(chunk => chunk.Id).ValueGeneratedNever();

        // Fixed length because a SHA-256 digest has one. Text rather than `bytea` for the reason the value object
        // states: this digest is compared and read, unlike the raw MIME digest that only ever round-trips.
        entity.Property(chunk => chunk.ContentHash)
            .HasMaxLength(EmailChunkContentHash.Length)
            .IsFixedLength()
            .IsRequired();

        entity.Property(chunk => chunk.Text).IsRequired();

        entity.HasIndex(chunk => new { chunk.StoredEmailId, chunk.Ordinal })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName);

        entity.HasOne(chunk => chunk.StoredEmail)
            .WithMany(email => email.Chunks)
            .HasForeignKey(chunk => chunk.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

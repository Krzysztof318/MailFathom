// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
        entity.ToTable("email_message_contents");
        entity.HasKey(content => content.StoredEmailId);
        entity.Property(content => content.StoredEmailId).ValueGeneratedNever();
        entity.Property(content => content.RawMime).HasColumnType("bytea").IsRequired();
        entity.Property(content => content.Sha256Hash).HasColumnType("bytea").IsRequired();
        entity.HasOne(content => content.StoredEmail)
            .WithOne(email => email.Content)
            .HasForeignKey<EmailMessageContentEntity>(content => content.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

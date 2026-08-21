// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Answering.Configurations;

/// <summary>Declares the messages one answered question named.</summary>
/// <remarks>
/// A table rather than an array of identifiers on the entry, which is the whole reason the answering record is split in
/// two. The entry states that a question was answered and stays true after the account is gone; these rows name
/// individual messages, and only a row with a foreign key onto the email inherits that message's erasure obligation.
/// </remarks>
internal sealed class MailAnsweringAuditedEmailConfiguration : IEntityTypeConfiguration<MailAnsweringAuditedEmailEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAnsweringAuditedEmailEntity> entity)
    {
        entity.ToTable("mail_answering_audited_emails");

        // The pair is the key rather than a surrogate, because one run names one message once however many of its
        // lookups found it. That makes the uniqueness the identity instead of a constraint beside one.
        entity.HasKey(read => new { read.MailAnsweringAuditEntryId, read.StoredEmailId });

        entity.HasOne<MailAnsweringAuditEntryEntity>()
            .WithMany(record => record.Emails)
            .HasForeignKey(read => read.MailAnsweringAuditEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        // No navigation to the stored email, so appending a row reads nothing: what is recorded is that a run
        // retrieved this identifier. The cascade is the point of the association — an erased message reaches every
        // run that read it, through the email's own deletion path rather than through a rule somebody remembers.
        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(read => read.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

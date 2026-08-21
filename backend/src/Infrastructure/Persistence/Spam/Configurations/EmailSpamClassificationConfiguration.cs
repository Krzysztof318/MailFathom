// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Spam.Configurations;

/// <summary>Declares what classification concluded about one email occurrence.</summary>
/// <remarks>
/// <para>
/// The table cascades from the email, which is what keeps derived data inside whatever erasure and retention reach the
/// mail it describes: nothing has to remember to delete a classification, and nothing can leave one behind describing a
/// message that is gone.
/// </para>
/// <para>
/// Every enumeration is stored as text for the reason each other outcome here is: it stays readable in an ad-hoc query
/// and survives a later reordering of the enum.
/// </para>
/// </remarks>
internal sealed class EmailSpamClassificationConfiguration : IEntityTypeConfiguration<EmailSpamClassificationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailSpamClassificationEntity> entity)
    {
        entity.ToTable("email_spam_classifications");
        entity.HasKey(classification => classification.StoredEmailId)
            .HasName(PersistenceConstraintNames.EmailSpamClassificationPrimaryKeyConstraintName);
        entity.Property(classification => classification.StoredEmailId).ValueGeneratedNever();
        entity.Property(classification => classification.Verdict).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(classification => classification.DecidedBy).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(classification => classification.CorpusRevision)
            .HasMaxLength(EmailSpamClassificationEntity.MaximumCorpusRevisionLength);
        entity.Property(classification => classification.Profile)
            .HasMaxLength(EmailSpamClassificationEntity.ProfileLength)
            .IsFixedLength();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(classification => classification.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(classification => classification.StoredEmail)
            .WithOne(email => email.SpamClassification)
            .HasForeignKey<EmailSpamClassificationEntity>(classification => classification.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

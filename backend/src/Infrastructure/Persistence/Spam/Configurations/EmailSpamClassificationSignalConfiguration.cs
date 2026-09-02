// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Spam.Configurations;

/// <summary>Declares the facts one classification rested on.</summary>
/// <remarks>
/// <para>
/// The signals cascade from the classification rather than from the email, so replacing a verdict replaces the facts it
/// rested on in one statement. Keeping a superseded verdict's signals beside the new ones would leave a record nobody
/// could read.
/// </para>
/// <para>
/// Every enumeration is stored as text for the reason each other outcome here is: it stays readable in an ad-hoc query
/// and survives a later reordering of the enum.
/// </para>
/// </remarks>
internal sealed class EmailSpamClassificationSignalConfiguration : IEntityTypeConfiguration<EmailSpamClassificationSignalEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailSpamClassificationSignalEntity> entity)
    {
        entity.ToTable("email_spam_classification_signals");
        entity.HasKey(signal => signal.Id);
        entity.Property(signal => signal.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(signal => signal.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(signal => signal.Name)
            .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumNameLength)
            .IsRequired();
        entity.Property(signal => signal.Observation)
            .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumObservationLength);
        entity.Property(signal => signal.Origin)
            .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumOriginLength)
            .IsRequired();

        entity.HasIndex(signal => new { signal.StoredEmailId, signal.Ordinal })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.EmailSpamClassificationSignalOrdinalUniqueIndexName);

        entity.HasOne(signal => signal.Classification)
            .WithMany(classification => classification.Signals)
            .HasForeignKey(signal => signal.StoredEmailId)
            .HasConstraintName(PersistenceConstraintNames.EmailSpamClassificationSignalForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

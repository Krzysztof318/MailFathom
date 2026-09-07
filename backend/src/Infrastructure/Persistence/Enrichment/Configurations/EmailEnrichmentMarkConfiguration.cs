// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Enrichment.Configurations;

/// <summary>Declares the readings one derivation produced.</summary>
/// <remarks>
/// <para>
/// The marks cascade from the derivation rather than from the email, so replacing a derivation replaces its readings in
/// one statement. Keeping a superseded derivation's marks beside the new ones would leave a record nobody could read.
/// </para>
/// <para>
/// One mark per aspect is a unique index rather than a check the writer performs, because two runs can reach one
/// message and neither sees the other's uncommitted write. What the index makes impossible is a row carrying two
/// answers to *what is this about*, which a screen would have to choose between.
/// </para>
/// <para>
/// Both enumerations are stored as text for the reason every other outcome here is: they stay readable in an ad-hoc
/// query and survive a later reordering of the enum. That matters most for the source, which is the column somebody
/// diagnosing a wrong mark selects on.
/// </para>
/// </remarks>
internal sealed class EmailEnrichmentMarkConfiguration : IEntityTypeConfiguration<EmailEnrichmentMarkEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailEnrichmentMarkEntity> entity)
    {
        entity.ToTable("email_enrichment_marks");
        entity.HasKey(mark => mark.Id);
        entity.Property(mark => mark.Aspect).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(mark => mark.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(mark => mark.Text)
            .HasMaxLength(EmailEnrichmentMarkEntity.MaximumTextLength)
            .IsRequired();
        entity.Property(mark => mark.Reason)
            .HasMaxLength(EmailEnrichmentMarkEntity.MaximumTextLength)
            .IsRequired();
        entity.Property(mark => mark.Origin)
            .HasMaxLength(EmailEnrichmentMarkEntity.MaximumOriginLength)
            .IsRequired();
        entity.Property(mark => mark.Evidence).IsRequired();

        entity.HasIndex(mark => new { mark.StoredEmailId, mark.Aspect })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.EmailEnrichmentMarkAspectUniqueIndexName);

        entity.HasOne(mark => mark.Enrichment)
            .WithMany(enrichment => enrichment.Marks)
            .HasForeignKey(mark => mark.StoredEmailId)
            .HasConstraintName(PersistenceConstraintNames.EmailEnrichmentMarkForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

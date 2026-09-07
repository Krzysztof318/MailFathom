// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Enrichment.Configurations;

/// <summary>Declares that a derivation has been made about one email occurrence.</summary>
/// <remarks>
/// The table cascades from the email, which is what keeps derived data inside whatever erasure and retention reach the
/// mail it describes: nothing has to remember to delete a derivation, and nothing can leave one behind describing a
/// message that is gone.
/// </remarks>
internal sealed class EmailEnrichmentConfiguration : IEntityTypeConfiguration<EmailEnrichmentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailEnrichmentEntity> entity)
    {
        entity.ToTable("email_enrichments");
        entity.HasKey(enrichment => enrichment.StoredEmailId)
            .HasName(PersistenceConstraintNames.EmailEnrichmentPrimaryKeyConstraintName);
        entity.Property(enrichment => enrichment.StoredEmailId).ValueGeneratedNever();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(enrichment => enrichment.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(enrichment => enrichment.StoredEmail)
            .WithOne(email => email.Enrichment)
            .HasForeignKey<EmailEnrichmentEntity>(enrichment => enrichment.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

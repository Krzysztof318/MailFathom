// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the outstanding request to re-read one email whose stored content a read found unusable.</summary>
/// <remarks>
/// One row per email, keyed by that email so a reader meeting the same damage repeatedly leaves one outstanding
/// request rather than a row per attempt. It is deliberately a table of its own rather than columns on the email: the
/// requests are sparse, they are read as a work list, and a repair that succeeds deletes a row instead of nulling four
/// columns on a row it must not otherwise touch.
/// </remarks>
internal sealed class EmailContentRepairRequestConfiguration : IEntityTypeConfiguration<EmailContentRepairRequestEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailContentRepairRequestEntity> entity)
    {
        entity.ToTable("email_content_repair_requests");
        entity.HasKey(repairRequest => repairRequest.StoredEmailId);
        entity.Property(repairRequest => repairRequest.StoredEmailId).ValueGeneratedNever();

        // Stored as text for the reason the content-availability reason is: the defect stays readable in an audit
        // query and survives any later reordering of the enum.
        entity.Property(repairRequest => repairRequest.Defect).HasConversion<string>().HasMaxLength(64).IsRequired();

        entity.HasOne(repairRequest => repairRequest.StoredEmail)
            .WithOne(email => email.ContentRepairRequest)
            .HasForeignKey<EmailContentRepairRequestEntity>(repairRequest => repairRequest.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

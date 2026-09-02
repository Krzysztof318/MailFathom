// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the octets of one file staged against a draft.</summary>
/// <remarks>
/// A one-to-one table whose primary key is also its foreign key, which is the arrangement every payload table here
/// uses and for the same reason: keeping the large binary value out of the description means listing what a draft
/// carries never loads a single file's bytes. The cascade is the erasure obligation, and it reaches this row through
/// the description's own cascade from the draft.
/// </remarks>
internal sealed class MailDraftAttachmentContentConfiguration
    : IEntityTypeConfiguration<MailDraftAttachmentContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftAttachmentContentEntity> entity)
    {
        entity.ToTable("mail_draft_attachment_contents");
        entity.HasKey(content => content.MailDraftAttachmentId);
        entity.Property(content => content.MailDraftAttachmentId).ValueGeneratedNever();
        entity.Property(content => content.Content).IsRequired();

        entity.HasOne(content => content.Attachment)
            .WithOne(attachment => attachment.Content)
            .HasForeignKey<MailDraftAttachmentContentEntity>(content => content.MailDraftAttachmentId)
            .HasConstraintName(PersistenceConstraintNames.MailDraftAttachmentContentForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the files an author staged against a draft, without the octets they are made of.</summary>
/// <remarks>
/// <para>
/// A table beside the draft rather than a part of its composed message, because a file is uploaded once and every
/// later revision is composed with it. Its identity is a surrogate rather than the file's name, so an author who
/// attached the same name twice can take off the one they meant.
/// </para>
/// <para>
/// The cascade is the erasure obligation, the same one the draft's own message carries: deleting the draft destroys
/// everything staged against it, so a file an author uploaded cannot outlive the record that says whose it is.
/// </para>
/// </remarks>
internal sealed class MailDraftAttachmentConfiguration : IEntityTypeConfiguration<MailDraftAttachmentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftAttachmentEntity> entity)
    {
        entity.ToTable("mail_draft_attachments");
        entity.HasKey(attachment => attachment.Id);
        entity.Property(attachment => attachment.Id).ValueGeneratedNever();
        entity.Property(attachment => attachment.FileName)
            .HasMaxLength(MailDraftAttachment.MaximumFileNameLength)
            .IsRequired();
        entity.Property(attachment => attachment.MediaType)
            .HasMaxLength(MailDraftAttachment.MaximumMediaTypeLength)
            .IsRequired();

        entity.HasOne(attachment => attachment.MailDraft)
            .WithMany(draft => draft.Attachments)
            .HasForeignKey(attachment => attachment.MailDraftId)
            .HasConstraintName(PersistenceConstraintNames.MailDraftAttachmentForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The order a composition attaches the files in, declared as an index because every read of this table is one
        // draft's files in that order: nothing here is ever read across drafts.
        entity.HasIndex(attachment => new { attachment.MailDraftId, attachment.StagedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailDraftAttachmentDraftIndexName);
    }
}

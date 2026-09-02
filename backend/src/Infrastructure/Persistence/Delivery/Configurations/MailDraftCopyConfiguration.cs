// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the copies of a draft this deployment put into the owner's drafts folder.</summary>
/// <remarks>
/// <para>
/// One row per revision, which is what makes a replacement expressible at all. IMAP has no command that changes a
/// stored message, so a new version of a draft is a new message beside the old one and the old one is removed
/// afterwards — and between those two commands the folder holds two copies that both belong to this draft. Keying
/// by the revision is also what makes the append idempotent without a read-then-write: appending the same revision
/// twice is refused by the key rather than by a check two callers can pass between.
/// </para>
/// <para>
/// The row is written before the <c>APPEND</c> goes out and completed after it, which is why the stage is a column
/// rather than the presence of a placement. A process that died between the command and its answer left a row
/// saying a copy may be there, and nothing appends that revision again on the strength of it.
/// </para>
/// <para>
/// The cascade is the erasure obligation the content table carries: erasing a draft erases what it says about the
/// mailbox with it. Nothing here is mail content — a folder, an alias, a UID, and an identity MailFathom minted are
/// its own or the server's names for things.
/// </para>
/// </remarks>
internal sealed class MailDraftCopyConfiguration : IEntityTypeConfiguration<MailDraftCopyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftCopyEntity> entity)
    {
        entity.ToTable("mail_draft_copies");
        entity.HasKey(copy => new { copy.MailDraftId, copy.Revision })
            .HasName(PersistenceConstraintNames.MailDraftCopyPrimaryKeyConstraintName);
        entity.Property(copy => copy.FolderAlias).HasMaxLength(128).IsRequired();
        entity.Property(copy => copy.FolderPath)
            .HasMaxLength(MailDraftCopyEntity.MaximumFolderPathLength)
            .IsRequired();
        entity.Property(copy => copy.InternetMessageId)
            .HasMaxLength(MailDraftCopyEntity.MaximumInternetMessageIdLength);

        entity.Property(copy => copy.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();

        // A copy is confirmed and withdrawn without the draft above it changing, so the draft's token would not
        // notice two passes settling one copy differently — and what that decides is whether a message is left in
        // somebody's folder.
        entity.Property(copy => copy.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(copy => copy.MailDraft)
            .WithMany(draft => draft.Copies)
            .HasForeignKey(copy => copy.MailDraftId)
            .HasConstraintName(PersistenceConstraintNames.MailDraftCopyForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

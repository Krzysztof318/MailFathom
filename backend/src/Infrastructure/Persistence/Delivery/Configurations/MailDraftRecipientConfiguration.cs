// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the people one draft is addressed to, which may be nobody at all.</summary>
/// <remarks>
/// <para>
/// Keyed by the draft and the position in its list, for the reason an outgoing recipient is: an address is personal
/// data and a key is repeated into every index over a table, and the ordinal keeps the recipients in the order the
/// composed message writes its headers in.
/// </para>
/// <para>
/// Nothing here carries a status or a reply code, unlike a send's recipients, because a draft has been offered to
/// nobody. A revision replaces the whole list rather than amending it, which is what keeps it the composed
/// message's own rather than an accumulation of everybody the draft was ever addressed to.
/// </para>
/// <para>
/// What it does carry and a send's recipients do not is where the address came from. A send meets the authored
/// governance before its row is written; a draft meets it at the promotion, which has only this row to read the
/// question off.
/// </para>
/// </remarks>
internal sealed class MailDraftRecipientConfiguration : IEntityTypeConfiguration<MailDraftRecipientEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftRecipientEntity> entity)
    {
        entity.ToTable("mail_draft_recipients");
        entity.HasKey(recipient => new { recipient.MailDraftId, recipient.Ordinal });
        entity.Property(recipient => recipient.Address)
            .HasMaxLength(OutgoingRecipient.MaximumAddressLength)
            .IsRequired();

        entity.Property(recipient => recipient.Role).HasConversion<string>().HasMaxLength(64).IsRequired();

        // As a name for the reason the role and every other stored enum is one: a row read years later says what it
        // meant, and the value survives a member being added ahead of it. The database default is the strict
        // reading, because that is what is true of a row written before this deployment kept the answer: nothing
        // recorded who chose the address, so the promotion treats it as the caller's own word.
        entity.Property(recipient => recipient.Provenance)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(AuthoredRecipientProvenance.NamedByCaller)
            .IsRequired();

        entity.HasOne(recipient => recipient.MailDraft)
            .WithMany(draft => draft.Recipients)
            .HasForeignKey(recipient => recipient.MailDraftId)
            .HasConstraintName(PersistenceConstraintNames.MailDraftRecipientForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

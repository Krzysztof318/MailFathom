// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the messages this deployment holds that have not been sent and may never be.</summary>
/// <remarks>
/// <para>
/// A table of its own rather than a stage of the outgoing record, because a draft has none of what that record is
/// for: no delivery, no recipient that has to resolve, no idempotency identity against a duplicate nobody could
/// withdraw, and no terminal stage. What it has instead is the one thing a send never does — a copy on somebody
/// else's server that every revision has to replace.
/// </para>
/// <para>
/// No unique identity is declared over the author, deliberately, and that is the difference from the outgoing
/// record's index rather than an omission. Two identical requests to save a draft are two drafts, because a draft
/// that turned out to exist twice costs its owner a deletion while a send that did costs a recipient a message they
/// read as sent twice.
/// </para>
/// </remarks>
internal sealed class MailDraftConfiguration : IEntityTypeConfiguration<MailDraftEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftEntity> entity)
    {
        entity.ToTable("mail_drafts");
        entity.HasKey(draft => draft.Id);
        entity.Property(draft => draft.Id).ValueGeneratedNever();
        entity.Property(draft => draft.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(draft => draft.RequesterIdentity)
            .HasMaxLength(OutgoingEmailRequester.MaximumIdentityLength)
            .IsRequired();

        // Stored as text for the reason every enum on the delivery feature is: both stay readable in an ad-hoc
        // audit query and survive any later reordering of their enum.
        entity.Property(draft => draft.RequesterOrigin).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(draft => draft.DivergenceReason).HasConversion<string>().HasMaxLength(64);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(draft => draft.ConcurrencyVersion).IsRowVersion();

        entity.HasIndex(draft => new { draft.OwnerId, draft.MailboxAccountId, draft.RevisedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailDraftAccountIndexName);

        entity.HasIndex(draft => draft.PromotedToOutgoingEmailId)
            .HasDatabaseName(PersistenceConstraintNames.MailDraftPromotedIndexName)
            .HasFilter($"\"{nameof(MailDraftEntity.PromotedToOutgoingEmailId)}\" IS NOT NULL");
    }
}

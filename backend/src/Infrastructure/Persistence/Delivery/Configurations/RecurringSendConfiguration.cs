// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the messages an owner asked to have sent again, and the repetitions they named.</summary>
/// <remarks>
/// <para>
/// A table rather than a section of the deployment's configuration, because a declaration is state: it is made by
/// the mailbox's owner out of a message they wrote, and it is stopped by them. The identity it is refused a
/// duplicate of is the sending account and the authoring act, exactly as an outgoing record's is and for the same
/// reason — a retried command must read back what it already declared rather than double what a mailbox sends.
/// </para>
/// <para>
/// A stopped declaration keeps its row. What it last did and when it was stopped are the account of a mailbox that
/// used to send something every week, and deleting the row would make that indistinguishable from a repetition
/// nobody ever declared.
/// </para>
/// </remarks>
internal sealed class RecurringSendConfiguration : IEntityTypeConfiguration<RecurringSendEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecurringSendEntity> entity)
    {
        entity.ToTable("recurring_sends");
        entity.HasKey(declaration => declaration.Id);
        entity.Property(declaration => declaration.Id).ValueGeneratedNever();
        entity.Property(declaration => declaration.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(declaration => declaration.RequesterIdentity)
            .HasMaxLength(OutgoingEmailRequester.MaximumIdentityLength)
            .IsRequired();
        entity.Property(declaration => declaration.Schedule)
            .HasMaxLength(RecurringSend.MaximumScheduleLength)
            .IsRequired();

        // Stored as text for the reason the outgoing record's origin is: it stays readable in an ad-hoc audit query
        // and survives any later reordering of the enum.
        entity.Property(declaration => declaration.RequesterOrigin)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(declaration => declaration.ConcurrencyVersion).IsRowVersion();

        entity.HasIndex(declaration => new
        {
            declaration.OwnerId,
            declaration.MailboxAccountId,
            declaration.RequesterOrigin,
            declaration.RequesterIdentity,
        })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.RecurringSendIdentityUniqueIndexName);

        // Filtered to the declarations that still produce occurrences and ordered the way the dispatch reads them,
        // so a pass over what repeats is a range read rather than a scan over every repetition ever declared.
        entity.HasIndex(declaration => declaration.DeclaredAt)
            .HasDatabaseName(PersistenceConstraintNames.RecurringSendActiveIndexName)
            .HasFilter($"\"{nameof(RecurringSendEntity.CancelledAt)}\" IS NULL");
    }
}

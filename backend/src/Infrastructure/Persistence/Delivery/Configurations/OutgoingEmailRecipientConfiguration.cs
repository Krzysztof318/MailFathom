// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the people one outgoing email is offered to, and what the server said about each.</summary>
/// <remarks>
/// <para>
/// A separate table rather than arrays on the record, because each recipient carries state that changes on its own:
/// a message is offered per address and answered per address, so a mistyped address among five must not stop the
/// other four and the four who received it must not be offered it again when the fifth is retried.
/// </para>
/// <para>
/// Keyed by the record and the position in its recipient list. An address is personal data and a key is repeated
/// into every index over a table, so the ordinal keys the row instead — and it keeps the recipients in the order the
/// request named them, which is the order a composed message writes its headers in.
/// </para>
/// </remarks>
internal sealed class OutgoingEmailRecipientConfiguration : IEntityTypeConfiguration<OutgoingEmailRecipientEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutgoingEmailRecipientEntity> entity)
    {
        entity.ToTable("outgoing_email_recipients");
        entity.HasKey(recipient => new { recipient.OutgoingEmailId, recipient.Ordinal });
        entity.Property(recipient => recipient.Address)
            .HasMaxLength(OutgoingRecipient.MaximumAddressLength)
            .IsRequired();

        // The contact the address was resolved from is deliberately left with no relationship, no constraint, and no
        // index of its own. It records which person this message was addressed by naming, so a contact amended or
        // erased afterwards must not change what was sent, and nothing ever looks a send up by it: the column is read
        // back with the record it belongs to, exactly as the address beside it is.

        // Stored as text for the reason every other enum on this feature is, and required on both: a row whose text
        // names no declared value fails the read rather than being taken as a neighbouring one by elimination.
        entity.Property(recipient => recipient.Role).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(recipient => recipient.Status).HasConversion<string>().HasMaxLength(64).IsRequired();

        // A recipient row is mutated on its own — an attempt answers about this address without touching the record
        // above it — so the record's token would not notice two attempts settling one recipient differently.
        entity.Property(recipient => recipient.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(recipient => recipient.OutgoingEmail)
            .WithMany(message => message.Recipients)
            .HasForeignKey(recipient => recipient.OutgoingEmailId)
            .HasConstraintName(PersistenceConstraintNames.OutgoingEmailRecipientForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

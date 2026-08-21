// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the people every occurrence of one recurring send is offered to.</summary>
/// <remarks>
/// A separate table for the reason an outgoing record's recipients are one, minus the state: nothing is ever
/// answered about a declaration's recipient, because a declaration transmits nothing. What the rows hold is the
/// envelope every occasion is built from, in the order the declaration named them, which is also the order the
/// occasion's composed message writes its headers in.
/// </remarks>
internal sealed class RecurringSendRecipientConfiguration : IEntityTypeConfiguration<RecurringSendRecipientEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecurringSendRecipientEntity> entity)
    {
        entity.ToTable("recurring_send_recipients");
        entity.HasKey(recipient => new { recipient.RecurringSendId, recipient.Ordinal });
        entity.Property(recipient => recipient.Address)
            .HasMaxLength(OutgoingRecipient.MaximumAddressLength)
            .IsRequired();

        entity.Property(recipient => recipient.Role).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(recipient => recipient.ConcurrencyVersion).IsRowVersion();

        entity.HasOne(recipient => recipient.RecurringSend)
            .WithMany(declaration => declaration.Recipients)
            .HasForeignKey(recipient => recipient.RecurringSendId)
            .HasConstraintName(PersistenceConstraintNames.RecurringSendRecipientForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Contacts.Configurations;

/// <summary>Declares the addresses one person uses, one address to one person within one owner's book.</summary>
/// <remarks>
/// The addresses are rows rather than an array column, which is what makes both rules over them structural. One address
/// belongs to one person, enforced across one owner's book rather than within a contact, because a book that let two
/// records claim one mailbox could not answer who a message is from; and erasing a person takes their addresses with
/// them through the foreign key rather than through a second statement somebody remembers to write. The key carries
/// the owner as well as the contact, so the owner a row is filed under is the one its contact is filed under.
/// </remarks>
internal sealed class ContactAddressConfiguration : IEntityTypeConfiguration<ContactAddressEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactAddressEntity> entity)
    {
        entity.ToTable("contact_addresses");
        entity.HasKey(address => address.Id);
        entity.Property(address => address.Id).ValueGeneratedNever();
        entity.Property(address => address.Address)
            .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
            .IsRequired();
        entity.Property(address => address.NormalizedAddress)
            .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
            .IsRequired();

        // No concurrency token of its own, which ADR 0001 asks to be justified rather than assumed. An address row
        // is only ever written by an amendment of the contact it hangs on, in the same transaction and the same
        // batch as that contact's own tokened update — the amendment stamps AmendedAt on every path — so the parent
        // row is what a competing write loses on, and a token here would only repeat that decision on a row that is
        // never reached on its own.

        // Unique within one owner's book rather than within one contact or across the table, and named because a
        // losing writer is recognized by the constraint its insert violated: two callers claiming one address is a
        // race whose retry resolves into the answer naming whoever holds it, not a failure to report. The owner leads
        // it, so the lookup from an address to a person reads one book rather than the table, and two owners may each
        // hold their own contact for one address.
        entity.HasIndex(address => new { address.OwnerId, address.NormalizedAddress })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.ContactAddressUniqueIndexName);

        // The owner travels in the key rather than beside it, which is what makes the repeated column derived: a row
        // can only carry the owner of the contact it hangs on, because no other pair exists to point at.
        entity.HasOne<ContactEntity>()
            .WithMany(contact => contact.Addresses)
            .HasForeignKey(address => new { address.ContactId, address.OwnerId })
            .HasPrincipalKey(contact => new { contact.Id, contact.OwnerId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

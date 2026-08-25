// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Contacts.Configurations;

/// <summary>Declares the people an owner's contact book holds.</summary>
/// <remarks>
/// <para>
/// The owner is a column and a foreign key rather than a property of the surface that reads the table, so erasing an
/// owner takes their book with it and every read leads with it. It is also an alternate key beside the identity, which
/// is what lets an address row's foreign key carry the owner and therefore be unable to name a different one.
/// </para>
/// <para>
/// The default address is a column on the person instead of a flag on each address. A flag would need a filtered unique
/// index to say that nobody has two, and that index refuses the intermediate row an update changing the choice passes
/// through; a column changes the choice in the same statement that records it. It carries no foreign key onto the
/// address row, because a key pointing back would make inserting either table first impossible.
/// </para>
/// <para>
/// The origin is held as its own name for the reason every bounded value beside it is, and the concurrency token is
/// there because a contact is amended in place — by the administration tool, by the MCP surface, and by collection — so
/// an amendment written from state read earlier has to fail rather than win.
/// </para>
/// </remarks>
internal sealed class ContactConfiguration : IEntityTypeConfiguration<ContactEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactEntity> entity)
    {
        entity.ToTable("contacts");
        entity.HasKey(contact => contact.Id);
        entity.Property(contact => contact.Id).ValueGeneratedNever();

        // The pair an address row's foreign key points at. The identity alone already identifies a contact, so this
        // adds no rule about contacts; what it adds is a key the dependent table can name the owner through, which is
        // what makes an address filed under a different owner than its contact impossible rather than unlikely.
        entity.HasAlternateKey(contact => new { contact.Id, contact.OwnerId });
        entity.Property(contact => contact.DisplayName)
            .HasMaxLength(ContactEntity.MaximumDisplayNameLength)
            .IsRequired();
        entity.Property(contact => contact.DisplayNameSortKey)
            .HasMaxLength(ContactEntity.MaximumDisplayNameLength)
            .UseCollation("C")
            .IsRequired();
        entity.Property(contact => contact.PreferredNormalizedAddress)
            .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
            .IsRequired();
        entity.Property(contact => contact.Note).HasMaxLength(ContactEntity.MaximumNoteLength);
        entity.Property(contact => contact.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(contact => contact.ConcurrencyVersion).IsRowVersion();

        // The one order a book is walked in, and the one a keyset page continues from. The owner leads it because a
        // page is always of one person's book, and the identity settles two people whose names compare equal, which
        // is what makes the order total within a book and the walk terminate. The sort key is pinned to the C
        // collation so that order is the ordinal one the domain derived the key to produce, rather than whichever
        // collation the database this runs on happens to have been created with.
        entity.HasIndex(contact => new { contact.OwnerId, contact.DisplayNameSortKey, contact.Id })
            .HasDatabaseName(PersistenceConstraintNames.ContactListingIndexName);

        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(contact => contact.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

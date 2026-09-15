// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Contacts.Configurations;

/// <summary>Declares the people a contact book holds, whether the book is a user's own or a mail account's.</summary>
/// <remarks>
/// <para>
/// The holder is a column rather than a property of the surface that reads the table, so every read leads with it, and
/// it is an alternate key beside the identity, which is what lets an address row's foreign key carry the book and
/// therefore be unable to name a different one. The two columns beside it are what makes each half erasable with its
/// holder, and each is a foreign key that cascades: a user's own book goes with the user record, and a mail account's
/// collected book goes with the account record beside that account's folders, threads, and jobs.
/// </para>
/// <para>
/// The check constraint is what stops the three from disagreeing. Exactly one of the two holders is present, the
/// holder column is that one prefixed with the kind of book it is, and the origin agrees with that kind — so a
/// collected contact filed under a user, an asserted one under a mailbox, or a row filed under a key its own holder
/// columns do not produce, is a row the database refuses rather than a state every reader has to reason about. The
/// prefix is where the two namespaces are kept apart, and <see cref="ContactBookHolder" /> holds why that matters.
/// </para>
/// <para>
/// The default address is a column on the person instead of a flag on each address. A flag would need a filtered unique
/// index to say that nobody has two, and that index refuses the intermediate row an update changing the choice passes
/// through; a column changes the choice in the same statement that records it. It carries no foreign key onto the
/// address row, because a key pointing back would make inserting either table first impossible.
/// </para>
/// <para>
/// The origin is held as its own name for the reason every bounded value beside it is, and the concurrency token is
/// there because a contact is amended in place — by the administration tool and by the MCP surface — so an amendment
/// written from state read earlier has to fail rather than win.
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

        entity.Property(contact => contact.BookHolderId)
            .HasMaxLength(ContactEntity.MaximumBookHolderLength)
            .IsRequired();
        entity.Property(contact => contact.MailboxAccountId)
            .HasMaxLength(ContactEntity.MaximumMailboxAccountIdLength);

        // The pair an address row's foreign key points at. The identity alone already identifies a contact, so this
        // adds no rule about contacts; what it adds is a key the dependent table can name the book through, which is
        // what makes an address filed under a different book than its contact impossible rather than unlikely.
        entity.HasAlternateKey(contact => new { contact.Id, contact.BookHolderId });
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

        // The one order a book is walked in, and the one a keyset page continues from. The holder leads it because a
        // page is read over a handful of named books, so each of them is reached by a seek rather than the table
        // scanned and narrowed; the identity settles two people whose names compare equal, which makes the order
        // total and the walk terminate. The sort key is pinned to the C collation so that order is the ordinal one
        // the domain derived the key to produce, rather than whichever collation the database this runs on happens
        // to have been created with.
        entity.HasIndex(contact => new { contact.BookHolderId, contact.DisplayNameSortKey, contact.Id })
            .HasDatabaseName(PersistenceConstraintNames.ContactListingIndexName);

        entity.ToTable(table => table.HasCheckConstraint(
            PersistenceConstraintNames.ContactBookHolderCheckConstraintName,
            $"""
             (("{nameof(ContactEntity.UserId)}" IS NOT NULL)::int + ("{nameof(ContactEntity.MailboxAccountId)}" IS NOT NULL)::int) = 1
             AND "{nameof(ContactEntity.BookHolderId)}" = CASE WHEN "{nameof(ContactEntity.UserId)}" IS NULL THEN '{ContactBookHolder.AccountKeyPrefix}' || "{nameof(ContactEntity.MailboxAccountId)}" ELSE '{ContactBookHolder.UserKeyPrefix}' || "{nameof(ContactEntity.UserId)}"::text END
             AND "{nameof(ContactEntity.Origin)}" = CASE WHEN "{nameof(ContactEntity.UserId)}" IS NULL THEN '{nameof(ContactOrigin.Collected)}' ELSE '{nameof(ContactOrigin.Asserted)}' END
             """));

        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(contact => contact.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The collected half hangs on the account the same way its folders, its threads, and its jobs do, so erasing a
        // mailbox takes what it picked up with the mail it picked it up from. Both keys are optional and the check
        // constraint above is what makes exactly one of them present on any row.
        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(contact => contact.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

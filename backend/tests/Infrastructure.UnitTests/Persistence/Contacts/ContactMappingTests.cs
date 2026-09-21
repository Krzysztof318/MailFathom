// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Contacts;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Contacts;

public sealed class ContactMappingTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The book of the user every asserted record here is written into.</summary>
    private static readonly ContactBookHolder OwnBook = ContactBookHolder.Of(SyntheticUser.Deployment);

    /// <summary>The book of the mail account every collected record here is written into.</summary>
    private static readonly ContactBookHolder AccountBook =
        ContactBookHolder.Of(SyntheticMailAccount.Deployment);

    /// <summary>What the rows hold is what the record stated, comparison forms and the chosen default included.</summary>
    [Fact]
    public void ToEntity_AContactWithANoteAndSeveralAddresses_KeepsEveryPartOfIt()
    {
        // Arrange
        var contact = ContactOf(
            "Anna Kowalska",
            ["Anna@Example.Test", "anna@personal.test"],
            preferred: "anna@personal.test",
            note: "Owes an answer.");

        // Act
        var entity = ContactMapping.ToEntity(OwnBook, contact);

        // Assert
        Assert.Equal("Anna Kowalska", entity.DisplayName);
        Assert.Equal("ANNA KOWALSKA", entity.DisplayNameSortKey);
        Assert.Equal("ANNA@PERSONAL.TEST", entity.PreferredNormalizedAddress);
        Assert.Equal("Owes an answer.", entity.Note);
        Assert.Equal(ContactOrigin.Asserted, entity.Origin);
        Assert.Equal(
            [("Anna@Example.Test", "ANNA@EXAMPLE.TEST"), ("anna@personal.test", "ANNA@PERSONAL.TEST")],
            entity.Addresses
                .Select(address => (address.Address, address.NormalizedAddress))
                .OrderBy(address => address.NormalizedAddress, StringComparer.Ordinal));
    }

    /// <summary>Every address row names the contact it belongs to, which is what the erasure cascade runs along.</summary>
    [Fact]
    public void ToEntity_AContact_TiesEveryAddressRowToIt()
    {
        // Arrange
        var contact = ContactOf("Anna Kowalska", ["anna@example.test", "anna@personal.test"]);

        // Act
        var entity = ContactMapping.ToEntity(OwnBook, contact);

        // Assert
        Assert.All(entity.Addresses, address => Assert.Equal(contact.Id.Value, address.ContactId));
        Assert.Equal(2, entity.Addresses.Select(address => address.Id).Distinct().Count());
    }

    /// <summary>Whose book a record belongs to is written onto the contact and onto every address row of it.</summary>
    /// <remarks>
    /// The address row carries the book key as well as the contact it hangs from, because uniqueness is over the book
    /// and the address and an index cannot reach through a foreign key to read one. An address row written under no
    /// book would take an address out of every other book.
    /// </remarks>
    [Fact]
    public void ToEntity_AContactOfOneUsersBook_WritesThatUserOntoTheRecordAndItsAddresses()
    {
        // Arrange
        var contact = ContactOf("Anna Kowalska", ["anna@example.test", "anna@personal.test"]);
        var holder = ContactBookHolder.Of(SyntheticUser.Another);

        // Act
        var entity = ContactMapping.ToEntity(holder, contact);

        // Assert
        Assert.Equal(SyntheticUser.Another.Value, entity.UserId);
        Assert.Null(entity.MailboxAccountId);
        Assert.Equal(holder.Key, entity.BookHolderId);
        Assert.All(entity.Addresses, address => Assert.Equal(holder.Key, address.BookHolderId));
    }

    /// <summary>A collected record is filed under the mail account it arrived on rather than under any user.</summary>
    /// <remarks>
    /// The column is what enrols the collected book in the account's own erasure walk, so a record written with the
    /// account left blank would outlive the mailbox it was derived from.
    /// </remarks>
    [Fact]
    public void ToEntity_AContactOfOneAccountsBook_WritesThatAccountAndNoUser()
    {
        // Arrange
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], origin: ContactOrigin.Collected);

        // Act
        var entity = ContactMapping.ToEntity(AccountBook, contact);

        // Assert
        Assert.Null(entity.UserId);
        Assert.Equal(SyntheticMailAccount.Deployment.Value, entity.MailboxAccountId);
        Assert.Equal(AccountBook.Key, entity.BookHolderId);
        Assert.All(entity.Addresses, address => Assert.Equal(AccountBook.Key, address.BookHolderId));
    }

    /// <summary>The kind of book decides the origin of what it holds, so a record of the other origin is refused rather than filed.</summary>
    /// <remarks>
    /// The check constraint underneath refuses the row, and answering here names the record rather than the column —
    /// which is what a caller can act on. Collection writing into a user's book is exactly what it stops.
    /// </remarks>
    [Theory]
    [InlineData(ContactOrigin.Collected, false)]
    [InlineData(ContactOrigin.Asserted, true)]
    public void ToEntity_ARecordOfTheOriginTheOtherBookHolds_IsRefused(ContactOrigin origin, bool intoAccountBook)
    {
        // Arrange
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], origin: origin);
        var holder = intoAccountBook ? AccountBook : OwnBook;

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactMapping.ToEntity(holder, contact));
    }

    /// <summary>A stored contact reads back as the record it was written from.</summary>
    [Fact]
    public void ToContact_RowsWrittenFromAContact_RebuildIt()
    {
        // Arrange
        var contact = ContactOf(
            "Anna Kowalska",
            ["anna@example.test", "anna@personal.test"],
            preferred: "anna@personal.test",
            note: "Owes an answer.",
            origin: ContactOrigin.Collected);

        // Act
        var rebuilt = ContactMapping.ToContact(ContactMapping.ToEntity(AccountBook, contact));

        // Assert
        Assert.Equal(contact.Id, rebuilt.Id);
        Assert.Equal(contact.DisplayName, rebuilt.DisplayName);
        Assert.Equal(contact.PreferredAddress, rebuilt.PreferredAddress);
        Assert.Equal(contact.Addresses, rebuilt.Addresses);
        Assert.Equal(contact.Note, rebuilt.Note);
        Assert.Equal(ContactOrigin.Collected, rebuilt.Origin);
        Assert.Equal(contact.RecordedAt, rebuilt.RecordedAt);
        Assert.Equal(contact.AmendedAt, rebuilt.AmendedAt);
    }

    /// <summary>A contact with no note reads back holding none rather than holding an empty one.</summary>
    [Fact]
    public void ToContact_ARowWithNoNote_HoldsNone()
    {
        // Arrange
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"]);

        // Act
        var rebuilt = ContactMapping.ToContact(ContactMapping.ToEntity(OwnBook, contact));

        // Assert
        Assert.Null(rebuilt.Note);
    }

    /// <summary>Which address is the default is the user's choice, so a row naming one it does not hold is refused rather than repaired.</summary>
    [Fact]
    public void ToContact_ARowNamingAPreferredAddressTheContactDoesNotHold_IsRefused()
    {
        // Arrange
        var entity = ContactMapping.ToEntity(OwnBook, ContactOf("Anna Kowalska", ["anna@example.test"]));
        entity.PreferredNormalizedAddress = "SOMEBODY.ELSE@EXAMPLE.TEST";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactMapping.ToContact(entity));
    }

    /// <summary>A row whose addresses have all gone is not a person the book can answer with.</summary>
    [Fact]
    public void ToContact_ARowWithNoAddress_IsRefused()
    {
        // Arrange
        var entity = ContactMapping.ToEntity(OwnBook, ContactOf("Anna Kowalska", ["anna@example.test"]));
        entity.Addresses.Clear();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactMapping.ToContact(entity));
    }

    /// <summary>An address row is filed under the write that added it, which for an amendment is years after the arrival.</summary>
    [Fact]
    public void ToAddressEntity_AnAddressAddedByAnAmendment_IsIdentifiedFromTheAmendmentRatherThanTheArrival()
    {
        // Arrange
        var amendedAt = RecordedAt.AddYears(1);
        var amended = ContactOf("Anna Kowalska", ["anna@example.test"]).AmendedWith(
            ContactDisplayName.Create("Anna Kowalska"),
            [Address("anna@example.test"), Address("anna@personal.test")],
            Address("anna@example.test"),
            note: null,
            amendedAt);

        // Act
        var row = ContactMapping.ToAddressEntity(OwnBook, amended, Address("anna@personal.test"));

        // Assert
        Assert.Equal(amendedAt, TimestampOf(row.Id));
        Assert.NotEqual(RecordedAt, TimestampOf(row.Id));
        Assert.Equal(OwnBook.Key, row.BookHolderId);
    }

    /// <summary>Reads back the instant a version 7 identifier was minted over, which is its leading 48 bits.</summary>
    private static DateTimeOffset TimestampOf(Guid identifier)
    {
        Span<byte> bytes = stackalloc byte[16];
        identifier.TryWriteBytes(bytes, bigEndian: true, out _);

        var milliseconds = ((long)BinaryPrimitives.ReadUInt32BigEndian(bytes) << 16)
            | BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    private static Contact ContactOf(
        string displayName,
        IReadOnlyList<string> addresses,
        string? preferred = null,
        string? note = null,
        ContactOrigin origin = ContactOrigin.Asserted) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create(displayName),
            [.. addresses.Select(Address)],
            Address(preferred ?? addresses[0]),
            note is null ? null : ContactNote.Create(note),
            origin,
            RecordedAt,
            RecordedAt);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}

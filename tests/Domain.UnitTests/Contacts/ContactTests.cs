// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

public sealed class ContactTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>One person's several addresses are one record, with the owner's choice of default kept as such.</summary>
    [Fact]
    public void Create_PersonUsingSeveralAddresses_HoldsThemAllWithThePreferredOneFirst()
    {
        // Arrange
        var work = Address("anna.kowalska@work.test");
        var personal = Address("anna@personal.test");
        var old = Address("a.kowalska@old.test");

        // Act
        var contact = ContactOf([work, personal, old], preferred: personal);

        // Assert
        Assert.Equal(personal, contact.PreferredAddress);
        Assert.Equal(
            [personal, old, work],
            contact.Addresses);
        Assert.True(contact.Holds(work));
    }

    /// <summary>Two spellings of one address inside one record name one mailbox, so they are merged rather than refused.</summary>
    [Fact]
    public void Create_OneAddressWrittenTwoWays_KeepsItOnce()
    {
        // Arrange
        var written = Address("Anna.Kowalska@Example.Test");
        var shouted = Address("ANNA.KOWALSKA@example.test");

        // Act
        var contact = ContactOf([written, shouted], preferred: shouted);

        // Assert
        Assert.Single(contact.Addresses);
        Assert.True(contact.Holds(Address("anna.kowalska@example.test")));
    }

    /// <summary>An address is looked up by the mailbox it names, never by the casing one sender happened to write.</summary>
    [Theory]
    [InlineData("anna.kowalska@example.test")]
    [InlineData("Anna.Kowalska@Example.Test")]
    [InlineData("ANNA.KOWALSKA@EXAMPLE.TEST")]
    public void Holds_AddressWrittenDifferently_FindsTheSameMailbox(string written)
    {
        // Arrange
        var contact = ContactOf([Address("anna.kowalska@example.test")]);

        // Act
        var holds = contact.Holds(Address(written));

        // Assert
        Assert.True(holds);
    }

    /// <summary>A local part that differs is a different mailbox, which is the edge the matching rule must not merge.</summary>
    [Fact]
    public void Holds_AddressDifferingInItsLocalPart_IsADifferentMailbox()
    {
        // Arrange
        var contact = ContactOf([Address("anna.kowalska@example.test")]);

        // Act
        var holds = contact.Holds(Address("anna.kowalski@example.test"));

        // Assert
        Assert.False(holds);
    }

    /// <summary>The person's name is the contact's, so the display name one message carried never enters the book.</summary>
    [Fact]
    public void Create_AddressCarryingASendersDisplayName_KeepsTheAddressAlone()
    {
        // Arrange
        EmailAddress.TryCreate("anna k.", "anna@example.test", out var fromAMessage);

        // Act
        var contact = ContactOf([fromAMessage], preferred: fromAMessage);

        // Assert
        Assert.Null(contact.PreferredAddress.DisplayName);
        Assert.Null(Assert.Single(contact.Addresses).DisplayName);
    }

    /// <summary>A contact without an address names nobody reachable, so the book refuses to hold one.</summary>
    [Fact]
    public void Create_NoAddress_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactOf([]));
    }

    /// <summary>A default the contact does not hold is not a choice, so the pair is refused rather than repaired.</summary>
    [Fact]
    public void Create_PreferredAddressTheContactDoesNotHold_IsRefused()
    {
        // Arrange
        var held = Address("anna@example.test");
        var other = Address("marek@example.test");

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactOf([held], preferred: other));
    }

    /// <summary>A person uses several mailboxes rather than an unbounded list of them, and the bound is on mailboxes.</summary>
    [Fact]
    public void Create_MoreAddressesThanAPersonMayHold_IsRefusedWhileTheBoundItselfIsAccepted()
    {
        // Arrange
        var atBound = AddressesNumbered(Contact.MaximumAddressCount);
        var overBound = AddressesNumbered(Contact.MaximumAddressCount + 1);

        // Act
        var accepted = ContactOf(atBound, preferred: atBound[0]);

        // Assert
        Assert.Equal(Contact.MaximumAddressCount, accepted.Addresses.Count);
        Assert.Throws<ArgumentException>(() => ContactOf(overBound, preferred: overBound[0]));
    }

    /// <summary>An address longer than SMTP admits is refused rather than dropped, because an owner typed it.</summary>
    [Fact]
    public void Create_AnAddressLongerThanTheBound_IsRefused()
    {
        // Arrange
        var localPart = new string('a', Contact.MaximumAddressLength - "@example.test".Length + 1);
        var overBound = Address($"{localPart}@example.test");

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactOf([overBound], preferred: overBound));
    }

    /// <summary>An origin nothing declares would be a claim no reader could interpret.</summary>
    [Fact]
    public void Create_OriginNothingDeclares_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Contact.Create(
            ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create("Anna Kowalska"),
            [Address("anna@example.test")],
            Address("anna@example.test"),
            note: null,
            (ContactOrigin)42,
            RecordedAt,
            RecordedAt));
    }

    /// <summary>An amendment states the record the owner wants, and keeps everything identity is made of.</summary>
    [Fact]
    public void AmendedWith_ANewNamePreferredAddressAndNote_KeepsIdentityOriginAndArrival()
    {
        // Arrange
        var work = Address("anna.kowalska@work.test");
        var personal = Address("anna@personal.test");
        var contact = ContactOf([work], preferred: work, origin: ContactOrigin.Collected);
        var amendedAt = RecordedAt.AddDays(2);

        // Act
        var amended = contact.AmendedWith(
            ContactDisplayName.Create("Anna Nowak"),
            [work, personal],
            personal,
            ContactNote.Create("Married name."),
            amendedAt);

        // Assert
        Assert.Equal(contact.Id, amended.Id);
        Assert.Equal(ContactOrigin.Collected, amended.Origin);
        Assert.Equal(contact.RecordedAt, amended.RecordedAt);
        Assert.Equal(amendedAt, amended.AmendedAt);
        Assert.Equal("Anna Nowak", amended.DisplayName.Value);
        Assert.Equal(personal, amended.PreferredAddress);
        Assert.Equal("Married name.", amended.Note?.Value);
    }

    /// <summary>Dropping every address is refused by the amendment for the reason creation refuses it.</summary>
    [Fact]
    public void AmendedWith_NoAddress_IsRefused()
    {
        // Arrange
        var contact = ContactOf([Address("anna@example.test")]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => contact.AmendedWith(
            contact.DisplayName,
            [],
            contact.PreferredAddress,
            note: null,
            RecordedAt.AddDays(1)));
    }

    /// <summary>Promotion is the one crossing between origins, and it is the act of taking the record on.</summary>
    [Fact]
    public void PromotedToAsserted_ACollectedContact_BecomesAssertedAndKeepsEverythingElse()
    {
        // Arrange
        var contact = ContactOf([Address("anna@example.test")], origin: ContactOrigin.Collected);
        var promotedAt = RecordedAt.AddDays(5);

        // Act
        var promoted = contact.PromotedToAsserted(promotedAt);

        // Assert
        Assert.Equal(ContactOrigin.Asserted, promoted.Origin);
        Assert.Equal(contact.Id, promoted.Id);
        Assert.Equal(contact.Addresses, promoted.Addresses);
        Assert.Equal(contact.RecordedAt, promoted.RecordedAt);
        Assert.Equal(promotedAt, promoted.AmendedAt);
    }

    /// <summary>Nothing can unsay that somebody wrote a person down, so an asserted contact has no promotion left.</summary>
    [Fact]
    public void PromotedToAsserted_AnAssertedContact_IsRefused()
    {
        // Arrange
        var contact = ContactOf([Address("anna@example.test")]);

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => contact.PromotedToAsserted(RecordedAt.AddDays(1)));
    }

    /// <summary>A writer amends the contacts of its own origin and no others, in both directions.</summary>
    [Theory]
    [InlineData(ContactOrigin.Asserted, ContactOrigin.Asserted, true)]
    [InlineData(ContactOrigin.Asserted, ContactOrigin.Collected, false)]
    [InlineData(ContactOrigin.Collected, ContactOrigin.Collected, true)]
    [InlineData(ContactOrigin.Collected, ContactOrigin.Asserted, false)]
    public void IsAmendableBy_AWriterOfEachOrigin_AnswersForItsOwnOriginAlone(
        ContactOrigin contactOrigin,
        ContactOrigin writer,
        bool expected)
    {
        // Arrange
        var contact = ContactOf([Address("anna@example.test")], origin: contactOrigin);

        // Act
        var amendable = contact.IsAmendableBy(writer);

        // Assert
        Assert.Equal(expected, amendable);
    }

    /// <summary>Only a writer acting for the owner promotes, whichever origin the contact itself carries.</summary>
    [Theory]
    [InlineData(ContactOrigin.Collected, ContactOrigin.Asserted, true)]
    [InlineData(ContactOrigin.Collected, ContactOrigin.Collected, false)]
    [InlineData(ContactOrigin.Asserted, ContactOrigin.Asserted, true)]
    [InlineData(ContactOrigin.Asserted, ContactOrigin.Collected, false)]
    public void IsPromotableBy_AWriterOfEachOrigin_AnswersForTheOwnersWriterAlone(
        ContactOrigin contactOrigin,
        ContactOrigin writer,
        bool expected)
    {
        // Arrange
        var contact = ContactOf([Address("anna@example.test")], origin: contactOrigin);

        // Act
        var promotable = contact.IsPromotableBy(writer);

        // Assert
        Assert.Equal(expected, promotable);
    }

    private static EmailAddress[] AddressesNumbered(int count) =>
        [.. Enumerable.Range(0, count).Select(number => Address($"anna{number}@example.test"))];

    private static EmailAddress Address(string address)
    {
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress);

        return emailAddress;
    }

    private static Contact ContactOf(
        IReadOnlyCollection<EmailAddress> addresses,
        EmailAddress? preferred = null,
        ContactOrigin origin = ContactOrigin.Asserted) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(RecordedAt)),
            ContactDisplayName.Create("Anna Kowalska"),
            addresses,
            preferred ?? addresses.FirstOrDefault(),
            note: null,
            origin,
            RecordedAt,
            RecordedAt);
}

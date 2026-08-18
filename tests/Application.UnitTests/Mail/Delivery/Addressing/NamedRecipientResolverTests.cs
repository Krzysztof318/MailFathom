// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Addressing;

/// <summary>Covers what happens between an author naming somebody and a message being addressed to an address.</summary>
/// <remarks>
/// The book is a real in-memory one rather than a substitute, because every claim here is about what a lookup of it
/// answers — one person, nobody, or several — and a substitute would let a test arrange an answer the book cannot give.
/// Every address belongs to a reserved test domain.
/// </remarks>
public sealed class NamedRecipientResolverTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Addressing somebody without saying which of their mailboxes uses the one the owner chose.</summary>
    [Fact]
    public async Task ResolveAsync_ContactNamedByIdentity_AddressesTheAddressTheyPrefer()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna.work@example.test", "anna@example.test");
        book.Hold(anna);

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, anna.Id)],
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(resolution.Recipients);
        Assert.True(resolution.IsResolved);
        Assert.Equal("anna.work@example.test", recipient.Address);
        Assert.Equal(anna.Id, recipient.Contact);
        Assert.Equal("Anna Kowalska", recipient.DisplayName);
    }

    /// <summary>A name belonging to one person addresses that person, whatever casing the author wrote it in.</summary>
    [Theory]
    [InlineData("Anna Kowalska")]
    [InlineData("anna kowalska")]
    public async Task ResolveAsync_NameOnePersonCarries_AddressesThatPerson(string writtenAs)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(anna);

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContactName(OutgoingRecipientRole.To, ContactDisplayName.Create(writtenAs))],
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(resolution.Recipients);
        Assert.Equal("anna@example.test", recipient.Address);
        Assert.Equal(anna.Id, recipient.Contact);
    }

    /// <summary>
    /// A name several people carry addresses nobody. Nothing ranks them, and the refusal says how many matched and
    /// nothing about any of them.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_NameSeveralPeopleCarry_IsRefusedAsAmbiguousNamingHowManyMatched()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna Kowalska", "anna@example.test"));
        book.Hold(ContactOf("Anna Kowalska", "anna.k@example.test"));
        book.Hold(ContactOf("Bruno Nowak", "bruno@example.test"));

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContactName(OutgoingRecipientRole.To, ContactDisplayName.Create("Anna Kowalska"))],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Empty(resolution.Recipients);
        var refusal = Assert.IsType<RecipientResolutionRefusal>(resolution.Refusal);
        Assert.Equal(RecipientResolutionRefusalReason.ContactNameAmbiguous, refusal.Reason);
        Assert.Equal(2, refusal.MatchedContactCount);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailContactNameAmbiguous, refusal.Failure);
    }

    /// <summary>An identity nothing answers to and a name nobody carries are the same refusal and carry no count.</summary>
    [Fact]
    public async Task ResolveAsync_ContactTheBookDoesNotHold_IsRefusedAsUnknown()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna Kowalska", "anna@example.test"));

        // Act
        var byIdentity = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, AContactId())],
            TestContext.Current.CancellationToken);

        var byName = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContactName(OutgoingRecipientRole.To, ContactDisplayName.Create("Nobody Here"))],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RecipientResolutionRefusalReason.ContactUnknown, byIdentity.Refusal?.Reason);
        Assert.Equal(RecipientResolutionRefusalReason.ContactUnknown, byName.Refusal?.Reason);
        Assert.Null(byIdentity.Refusal?.MatchedContactCount);
        Assert.Null(byName.Refusal?.MatchedContactCount);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailContactUnknown, byName.Refusal?.Failure);
    }

    /// <summary>An act may name another mailbox of theirs, and then that one is what the message is offered to.</summary>
    [Fact]
    public async Task ResolveAsync_ActNamingAnotherAddressTheContactHolds_AddressesThatOne()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna.work@example.test", "anna.home@example.test");
        book.Hold(anna);

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, anna.Id, "ANNA.HOME@example.test")],
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(resolution.Recipients);

        // The book's own spelling rather than the caller's, so the record carries what the owner wrote down.
        Assert.Equal("anna.home@example.test", recipient.Address);
        Assert.Equal(anna.Id, recipient.Contact);
    }

    /// <summary>
    /// Naming a contact must never reach a mailbox alongside them, so an address they do not hold is refused rather
    /// than sent to and rather than falling back to the one they prefer.
    /// </summary>
    [Theory]
    [InlineData("someone.else@example.test")]
    [InlineData("not-an-address")]
    public async Task ResolveAsync_ActNamingAnAddressTheContactDoesNotHold_IsRefused(string chosenAddress)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(anna);

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, anna.Id, chosenAddress)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Equal(RecipientResolutionRefusalReason.ContactAddressNotHeld, resolution.Refusal?.Reason);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailContactAddressNotHeld, resolution.Refusal?.Failure);
    }

    /// <summary>An address an author wrote reaches the composition exactly as they wrote it, and names no contact.</summary>
    [Fact]
    public async Task ResolveAsync_AddressTheAuthorSupplied_IsCarriedThroughUnparsed()
    {
        // Arrange
        var book = new InMemoryContactBookStore();

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.AtAddress(OutgoingRecipientRole.Cc, " Bruno@Example.test ", "Bruno Nowak")],
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(resolution.Recipients);
        Assert.Equal(" Bruno@Example.test ", recipient.Address);
        Assert.Equal("Bruno Nowak", recipient.DisplayName);
        Assert.Null(recipient.Contact);
    }

    /// <summary>The order the author wrote is the order the composition writes its headers in.</summary>
    [Fact]
    public async Task ResolveAsync_AddressesAndContactsTogether_KeepsTheOrderTheAuthorNamedThemIn()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(anna);

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [
                NamedRecipient.AtAddress(OutgoingRecipientRole.To, "first@example.test"),
                NamedRecipient.ByContact(OutgoingRecipientRole.Cc, anna.Id),
                NamedRecipient.AtAddress(OutgoingRecipientRole.Bcc, "last@example.test"),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["first@example.test", "anna@example.test", "last@example.test"],
            resolution.Recipients.Select(recipient => recipient.Address));
    }

    /// <summary>
    /// One recipient nobody can be found for refuses the whole message. Delivering to the rest would tell an author
    /// their message was sent while the person they cared about never receives it.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_OneRecipientRefused_RefusesTheWholeMessage()
    {
        // Arrange
        var book = new InMemoryContactBookStore();

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            [
                NamedRecipient.AtAddress(OutgoingRecipientRole.To, "reachable@example.test"),
                NamedRecipient.ByContact(OutgoingRecipientRole.Cc, AContactId()),
            ],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Empty(resolution.Recipients);
    }

    /// <summary>
    /// A message naming nobody resolves rather than refuses. Addressing nobody is what the composition refuses, on its
    /// own terms and against the field it names, so answering with a refusal here would report it as a contact problem.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_NobodyNamedAtAll_IsResolvedToNoRecipients()
    {
        // Arrange
        var resolver = new NamedRecipientResolver(new InMemoryContactBookStore());

        // Act
        RecipientResolution resolution = await resolver.ResolveAsync([], TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.IsResolved);
        Assert.Empty(resolution.Recipients);
        Assert.Null(resolution.Refusal);
    }

    /// <summary>
    /// Each contact named is a read of the book, so the count is bounded before the first of them. A list longer than an
    /// outgoing record can hold describes a send that could not be written down whatever the book answered.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_MoreRecipientsThanARecordCanHold_IsRefusedBeforeTheBookIsRead()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(contact);

        var beyondTheBound = Enumerable
            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
            .Select(_ => NamedRecipient.ByContact(OutgoingRecipientRole.To, contact.Id))
            .ToArray();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new NamedRecipientResolver(book).ResolveAsync(
                beyondTheBound,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, book.BatchedLookupCount);
    }

    /// <summary>
    /// The book is read once for the identities named and once for the names, so what a message costs to address follows
    /// from how it was addressed rather than from how many people it names.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ManyContactsNamedBothWays_ReadsTheBookOncePerWayTheyWereNamed()
    {
        // Arrange
        var book = new InMemoryContactBookStore();

        var held = Enumerable
            .Range(0, 20)
            .Select(number => ContactOf($"Correspondent {number:D2}", $"correspondent.{number:D2}@example.test"))
            .ToArray();

        foreach (var contact in held)
        {
            book.Hold(contact);
        }

        var namedBothWays = held
            .Select(contact => NamedRecipient.ByContact(OutgoingRecipientRole.To, contact.Id))
            .Concat(held.Select(contact => NamedRecipient.ByContactName(
                OutgoingRecipientRole.Cc,
                contact.DisplayName)))
            .ToArray();

        // Act
        var resolution = await new NamedRecipientResolver(book).ResolveAsync(
            namedBothWays,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(resolution.IsResolved);
        Assert.Equal(namedBothWays.Length, resolution.Recipients.Count);
        Assert.Equal(2, book.BatchedLookupCount);
    }

    /// <summary>Addressing somebody is not a fact about them, so the book is read and never written.</summary>
    [Fact]
    public async Task ResolveAsync_ContactAddressed_LeavesTheBookAsItWas()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(anna);

        // Act
        await new NamedRecipientResolver(book).ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, anna.Id)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, book.ContactCount);
        Assert.Equal(anna, Assert.Single(book.Contacts));
    }

    private static ContactId AContactId() => ContactId.Create(Guid.CreateVersion7(Recorded));

    private static Contact ContactOf(string displayName, params string[] addresses) => Contact.Create(
        AContactId(),
        ContactDisplayName.Create(displayName),
        [.. addresses.Select(Address)],
        Address(addresses[0]),
        note: null,
        ContactOrigin.Asserted,
        Recorded,
        Recorded);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}

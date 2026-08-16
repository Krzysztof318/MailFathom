// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

public sealed class ContactBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A recorded person is held with the identity the book minted and the instant it minted it at.</summary>
    [Fact]
    public async Task RecordAsync_APersonTheBookDoesNotHold_HoldsThemUnderAnIdentityOfItsOwn()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contactBook = BookOver(book);

        // Act
        var result = await contactBook.RecordAsync(
            NewContactOf("Anna Kowalska", ["anna@example.test", "anna.kowalska@work.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);

        var written = Assert.IsType<Contact>(result.Contact);

        Assert.NotEqual(Guid.Empty, written.Id.Value);
        Assert.Equal(Now, written.RecordedAt);
        Assert.Equal(Now, written.AmendedAt);
        Assert.Equal(ContactOrigin.Asserted, written.Origin);
        Assert.Equal(1, book.ContactCount);
    }

    /// <summary>One address belongs to one person, so the second claim is answered with who holds it.</summary>
    [Fact]
    public async Task RecordAsync_AnAddressAnotherContactHolds_IsRefusedAndNamesTheHolder()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contactBook = BookOver(book);
        var first = await contactBook.RecordAsync(
            NewContactOf("Anna Kowalska", ["anna@example.test"]),
            TestContext.Current.CancellationToken);

        // Act
        var second = await contactBook.RecordAsync(
            NewContactOf("Anna K.", ["ANNA@EXAMPLE.TEST"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.AddressHeldByAnotherContact, second.Outcome);
        Assert.Equal(first.Contact?.Id, second.AddressHolder);
        Assert.Equal(1, book.ContactCount);
    }

    /// <summary>Collection never edits what an owner wrote down, and an owner never edits a collected record in place.</summary>
    [Theory]
    [InlineData(ContactOrigin.Asserted, ContactOrigin.Collected)]
    [InlineData(ContactOrigin.Collected, ContactOrigin.Asserted)]
    public async Task AmendAsync_AWriterOfTheOtherOrigin_IsRefusedAndHandedTheRecordThatRefusedIt(
        ContactOrigin held,
        ContactOrigin writer)
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], held);
        book.Hold(contact);

        // Act
        var result = await BookOver(book).AmendAsync(
            AmendmentOf(contact, writer, "Anna Nowak", ["anna@example.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.OriginRefusesWriter, result.Outcome);
        Assert.Equal(held, result.Contact?.Origin);
        Assert.Equal("Anna Kowalska", result.Contact?.DisplayName.Value);
    }

    /// <summary>An amendment states the whole record, so keeping an address the contact already holds is no clash with itself.</summary>
    [Fact]
    public async Task AmendAsync_KeepingAnAddressItAlreadyHolds_IsWritten()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted);
        book.Hold(contact);

        var clock = new FakeTimeProvider(Now);
        clock.Advance(TimeSpan.FromHours(2));

        // Act
        var result = await BookOver(book, clock).AmendAsync(
            AmendmentOf(contact, ContactOrigin.Asserted, "Anna Nowak", ["anna@example.test", "anna@personal.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.Equal("Anna Nowak", result.Contact?.DisplayName.Value);
        Assert.Equal(2, result.Contact?.Addresses.Count);
        Assert.Equal(contact.RecordedAt, result.Contact?.RecordedAt);
        Assert.Equal(Now.AddHours(2), result.Contact?.AmendedAt);
    }

    /// <summary>An address a different person already uses is refused for an amendment exactly as it is for a record.</summary>
    [Fact]
    public async Task AmendAsync_AnAddressAnotherContactHolds_IsRefusedAndNamesTheHolder()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted);
        var marek = ContactOf("Marek Nowak", ["marek@example.test"], ContactOrigin.Asserted);
        book.Hold(anna);
        book.Hold(marek);

        // Act
        var result = await BookOver(book).AmendAsync(
            AmendmentOf(anna, ContactOrigin.Asserted, "Anna Kowalska", ["anna@example.test", "marek@example.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.AddressHeldByAnotherContact, result.Outcome);
        Assert.Equal(marek.Id, result.AddressHolder);
    }

    /// <summary>A contact nobody holds cannot be amended, and the answer says so rather than creating one.</summary>
    [Fact]
    public async Task AmendAsync_AContactTheBookDoesNotHold_IsNotFound()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var absent = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted);

        // Act
        var result = await BookOver(book).AmendAsync(
            AmendmentOf(absent, ContactOrigin.Asserted, "Anna Kowalska", ["anna@example.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.NotFound, result.Outcome);
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>Promotion is the act that makes a collected record the owner's, and it keeps everything else.</summary>
    [Fact]
    public async Task PromoteAsync_ACollectedContact_BecomesAsserted()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Collected);
        book.Hold(contact);

        // Act
        var result = await BookOver(book).PromoteAsync(
            contact.Id,
            ContactOrigin.Asserted,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.Equal(ContactOrigin.Asserted, result.Contact?.Origin);
        Assert.Equal(contact.DisplayName.Value, result.Contact?.DisplayName.Value);
    }

    /// <summary>Asking again after a promotion says nothing was left to do rather than writing the record twice.</summary>
    [Fact]
    public async Task PromoteAsync_AnAssertedContact_ReportsThereIsNothingToPromote()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted);
        book.Hold(contact);

        // Act
        var result = await BookOver(book).PromoteAsync(
            contact.Id,
            ContactOrigin.Asserted,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.AlreadyAsserted, result.Outcome);
        Assert.Equal(contact.Id, result.Contact?.Id);
    }

    /// <summary>Collection cannot award itself the authority promotion confers; only a writer acting for the owner promotes.</summary>
    [Fact]
    public async Task PromoteAsync_AWriterCollectingMail_IsRefusedTheContactItCollected()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Collected);
        book.Hold(contact);

        // Act
        var result = await BookOver(book).PromoteAsync(
            contact.Id,
            ContactOrigin.Collected,
            TestContext.Current.CancellationToken);

        // Assert
        var stillHeld = await book.FindAsync(contact.Id, TestContext.Current.CancellationToken);

        Assert.Equal(ContactWriteOutcome.OriginRefusesWriter, result.Outcome);
        Assert.Equal(ContactOrigin.Collected, stillHeld?.Origin);
    }

    /// <summary>Erasure reports what it removed, which is how the path is proven rather than described.</summary>
    [Fact]
    public async Task EraseAsync_AContactWithSeveralAddresses_RemovesThePersonAndSaysWhatWentWithThem()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contact = ContactOf(
            "Anna Kowalska",
            ["anna@example.test", "anna@personal.test", "a.kowalska@old.test"],
            ContactOrigin.Asserted);
        book.Hold(contact);

        var contactBook = BookOver(book);

        // Act
        var erasure = await contactBook.EraseAsync(contact.Id, TestContext.Current.CancellationToken);
        var afterwards = await contactBook.ExportAsync(contact.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ContactErasure(contact.Id, WasHeld: true, AddressesErased: 3), erasure);
        Assert.Null(afterwards);
        Assert.Equal(0, book.ContactCount);
    }

    /// <summary>Erasing somebody the book never held is the state the owner asked for rather than a failure.</summary>
    [Fact]
    public async Task EraseAsync_AContactTheBookDoesNotHold_IsAnAnswerRatherThanAFailure()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contactId = ContactId.Create(Guid.CreateVersion7(Now));

        // Act
        var erasure = await BookOver(book).EraseAsync(contactId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ContactErasure(contactId, WasHeld: false, AddressesErased: 0), erasure);
    }

    /// <summary>An export carries the whole record and the instant it was taken, which is what dates the answer.</summary>
    [Fact]
    public async Task ExportAsync_AContactWithANoteAndSeveralAddresses_CarriesEverythingHeld()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var contactBook = BookOver(book);
        var recorded = await contactBook.RecordAsync(
            new NewContact
            {
                DisplayName = ContactDisplayName.Create("Anna Kowalska"),
                Addresses = [Address("anna@example.test"), Address("anna@personal.test")],
                PreferredAddress = Address("anna@personal.test"),
                Note = ContactNote.Create("Owes an answer about the contract."),
                Origin = ContactOrigin.Collected,
            },
            TestContext.Current.CancellationToken);

        // Act
        var export = await contactBook.ExportAsync(
            recorded.Contact!.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(export);
        Assert.Equal(Now, export.ProducedAt);
        Assert.Equal("Anna Kowalska", export.Contact.DisplayName.Value);
        Assert.Equal("Owes an answer about the contract.", export.Contact.Note?.Value);
        Assert.Equal(ContactOrigin.Collected, export.Contact.Origin);
        Assert.Equal(Address("anna@personal.test"), export.Contact.PreferredAddress);
        Assert.Equal(
            ["ANNA@EXAMPLE.TEST", "ANNA@PERSONAL.TEST"],
            export.Contact.Addresses.Select(address => address.NormalizedAddress).Order(StringComparer.Ordinal));
    }

    /// <summary>Nothing is exported for a person the book does not hold.</summary>
    [Fact]
    public async Task ExportAsync_AContactTheBookDoesNotHold_ProducesNothing()
    {
        // Arrange
        var book = new InMemoryContactBookStore();

        // Act
        var export = await BookOver(book).ExportAsync(
            ContactId.Create(Guid.CreateVersion7(Now)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(export);
    }

    private static ContactBook BookOver(InMemoryContactBookStore book, FakeTimeProvider? clock = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = clock ?? new FakeTimeProvider(Now);

        return new ContactBook(
            book,
            book,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
            timeProvider);
    }

    private static NewContact NewContactOf(string displayName, IReadOnlyList<string> addresses) =>
        new()
        {
            DisplayName = ContactDisplayName.Create(displayName),
            Addresses = [.. addresses.Select(Address)],
            PreferredAddress = Address(addresses[0]),
            Origin = ContactOrigin.Asserted,
        };

    private static ContactAmendment AmendmentOf(
        Contact contact,
        ContactOrigin writer,
        string displayName,
        IReadOnlyList<string> addresses) =>
        new()
        {
            ContactId = contact.Id,
            Writer = writer,
            DisplayName = ContactDisplayName.Create(displayName),
            Addresses = [.. addresses.Select(Address)],
            PreferredAddress = Address(addresses[0]),
        };

    private static Contact ContactOf(string displayName, IReadOnlyList<string> addresses, ContactOrigin origin) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [.. addresses.Select(Address)],
            Address(addresses[0]),
            note: null,
            origin,
            Now,
            Now);

    private static EmailAddress Address(string address)
    {
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress);

        return emailAddress;
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
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
        var stillHeld = await book.FindAsync(SyntheticMailOwner.Deployment, contact.Id, TestContext.Current.CancellationToken);

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

    /// <summary>Reading the book is reading what was derived from somebody's mail, so it asks for the audit grant rather than the administrative read.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            book.ReadPageAsync(ContactQuery.Create(origin: null, search: null, pageSize: null, cursor: null), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminAuditRead, refusal.RequiredPermission);
    }

    /// <summary>Writing to the book is causing work rather than reading it, so reading grants nothing towards it.</summary>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyTheAuditRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var book = BookOver(
            store,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminAuditRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => book.RecordAsync(
            NewContactOf("Ada Lovelace", ["ada@example.test"]),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminOperate, refusal.RequiredPermission);
    }

    /// <summary>Taking somebody out of the book is destroying, which is the one grant allocated to that.</summary>
    [Fact]
    public async Task EraseAsync_ACallerGrantedOnlyTheAdministrativeOperate_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            book.EraseAsync(ContactId.Create(Guid.CreateVersion7(Now)), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
    }

    /// <summary>The protocol reaches the same writes the operator does, under the name its own surface publishes.</summary>
    /// <remarks>
    /// The two halves are disjoint, so a caller admitted by the MCP endpoint can never hold an administrative name
    /// however broadly its entry is granted. Requiring one would leave these acts reachable from <c>mfctl</c> and dead
    /// from every contact tool, which is the failure this asserts against.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyTheProtocolWrite_WritesTheContact()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsWrite));

        // Act
        var result = await book.RecordAsync(
            NewContactOf("Ada Lovelace", ["ada@example.test"]),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result.Contact);
    }

    /// <summary>Erasure is the other act both surfaces perform, and the alternative admits it under either name.</summary>
    [Fact]
    public async Task EraseAsync_ACallerGrantedOnlyTheProtocolWrite_IsNotRefusedByTheGrant()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsWrite));

        // Act
        var refusal = await Record.ExceptionAsync(() =>
            book.EraseAsync(ContactId.Create(Guid.CreateVersion7(Now)), TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>A collected record exists to be taken on, so both surfaces that write the book reach the act that does it.</summary>
    [Fact]
    public async Task PromoteAsync_ACallerGrantedEitherWrite_ReachesTheBook()
    {
        // Arrange
        var store = new InMemoryContactBookStore();

        // Act
        var byOperator = await BookOver(
                store,
                authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate))
            .PromoteAsync(
                ContactId.Create(Guid.CreateVersion7(Now)),
                ContactOrigin.Asserted,
                TestContext.Current.CancellationToken);
        var byAgent = await BookOver(
                store,
                authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsWrite))
            .PromoteAsync(
                ContactId.Create(Guid.CreateVersion7(Now)),
                ContactOrigin.Asserted,
                TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.NotFound, byOperator.Outcome);
        Assert.Equal(ContactWriteOutcome.NotFound, byAgent.Outcome);
    }

    /// <summary>Collection reading its own mail must not be able to award itself the authority promotion carries.</summary>
    [Fact]
    public async Task PromoteAsync_AWriterActingUnderTheCollectedOrigin_IsRefusedByTheRecordItNamed()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var collected = ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Collected);
        store.Hold(collected);

        var book = BookOver(
            store,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsWrite));

        // Act
        var promoted = await book.PromoteAsync(
            collected.Id,
            ContactOrigin.Collected,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.OriginRefusesWriter, promoted.Outcome);
        Assert.Equal(ContactOrigin.Collected, Assert.Single(store.Contacts).Origin);
    }

    /// <summary>Collection is work no caller requested, so it states MailFathom's own identity instead of holding a grant.</summary>
    [Fact]
    public async Task CollectAsync_ReachedAsThisProcessesOwnWork_RecordsUnderTheCollectedOrigin()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var book = BookOver(store, authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act
        var recorded = await book.CollectAsync(
            NewContactOf("Anna Kowalska", ["anna@example.test"]) with { Origin = ContactOrigin.Collected },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, recorded.Outcome);
        Assert.Equal(ContactOrigin.Collected, Assert.Single(store.Contacts).Origin);
    }

    /// <summary>A permission would make writing into the collected origin reachable by whoever an operator granted it to.</summary>
    [Fact]
    public async Task CollectAsync_ReachedByACallerHoldingEveryWritingGrant_IsRefused()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminOperate,
                MailFathomPermission.MailContactsWrite));

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => book.CollectAsync(
            NewContactOf("Anna Kowalska", ["anna@example.test"]) with { Origin = ContactOrigin.Collected },
            TestContext.Current.CancellationToken));
    }

    /// <summary>The one writer that could award itself an owner's authority must not be able to do it on the way in.</summary>
    [Fact]
    public async Task CollectAsync_ARecordNamingTheAssertedOrigin_IsRefused()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var book = BookOver(store, authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => book.CollectAsync(
            NewContactOf("Anna Kowalska", ["anna@example.test"]),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, store.ContactCount);
    }

    /// <summary>Collection asks whether an address is spoken for; handing it the record would put a person in reach of work that may not touch them.</summary>
    [Fact]
    public async Task HoldsAddressAsync_AnAddressTheBookHolds_AnswersWithoutProducingTheRecord()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted));

        var book = BookOver(store, authorization: AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Act & Assert
        Assert.True(await book.HoldsAddressAsync(Address("ANNA@example.test"), TestContext.Current.CancellationToken));
        Assert.False(await book.HoldsAddressAsync(Address("marek@example.test"), TestContext.Current.CancellationToken));
    }

    /// <summary>Everything collection built is a contact of its own origin, so an owner reversing their mind takes exactly that out.</summary>
    [Fact]
    public async Task EraseCollectedAsync_ABookOfBothOrigins_RemovesOnlyWhatWasCollected()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(ContactOf("Anna Kowalska", ["anna@example.test"], ContactOrigin.Asserted));
        store.Hold(ContactOf("Marek Nowak", ["marek@example.test", "m.nowak@work.test"], ContactOrigin.Collected));
        store.Hold(ContactOf("Ewa Lis", ["ewa@example.test"], ContactOrigin.Collected));

        var book = BookOver(store, authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminErase));

        // Act
        var erasure = await book.EraseCollectedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, erasure.ContactsErased);
        Assert.Equal(3, erasure.AddressesErased);
        Assert.Equal(ContactOrigin.Asserted, Assert.Single(store.Contacts).Origin);
    }

    /// <summary>Erasing a book that had collected nobody is the state the owner asked for rather than a failure.</summary>
    [Fact]
    public async Task EraseCollectedAsync_ABookThatCollectedNobody_ReportsNothingRemoved()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminErase));

        // Act
        var erasure = await book.EraseCollectedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, erasure.ContactsErased);
        Assert.Equal(0, erasure.AddressesErased);
    }

    /// <summary>Undoing what collection built is a disposal, so it is behind the erasing grant rather than the operating one.</summary>
    [Fact]
    public async Task EraseCollectedAsync_ACallerGrantedOnlyTheOperatingName_IsRefused()
    {
        // Arrange
        var book = BookOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminOperate));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            book.EraseCollectedAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminErase, refusal.RequiredPermission);
    }

    private static ContactBook BookOver(
        InMemoryContactBookStore book,
        FakeTimeProvider? clock = null,
        AccessAuthorization? authorization = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = clock ?? new FakeTimeProvider(Now);

        return new ContactBook(
            book,
            book,
            ContactBookOwnerships.ForTheServedOwner(),
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
            timeProvider,
            authorization ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.AdminAuditRead,
                MailFathomPermission.AdminOperate,
                MailFathomPermission.AdminErase));
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
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers that a book belongs to one user, over every way a use case reaches one.</summary>
/// <remarks>
/// Each test arranges two books and reaches one, because a scope is only observable where there is something outside it
/// to leak: a suite holding one user's contacts would pass identically against a book that scopes nothing. The books
/// are the real in-memory one rather than a substitute, so what is asserted is which user the use case asked for and
/// what a book keyed that way answers, rather than an answer a test arranged.
/// </remarks>
public sealed class ContactBookOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A listing is one user's own, so nobody else's correspondents are served with it.</summary>
    [Fact]
    public async Task ReadPageAsync_ABookEachOfTwoUsersHolds_ServesTheCallersOwnAndNoOther()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var theirs = ContactOf("Anna Kowalska", "anna@example.test");
        var ours = ContactOf("Marek Nowak", "marek@example.test");
        store.Hold(SyntheticMailUser.Deployment, theirs);
        store.Hold(SyntheticMailUser.Another, ours);

        var reader = ReaderOf(store, SyntheticMailUser.Another);

        // Act
        var page = await reader.ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        var served = Assert.Single(page.Contacts);
        Assert.Equal(ours.Id, served.Id);
    }

    /// <summary>An address only somebody else's book holds resolves to nobody rather than to their contact.</summary>
    [Fact]
    public async Task FindByAddressAsync_AnAddressOnlyAnotherUsersBookHolds_AnswersWithNobody()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(SyntheticMailUser.Deployment, ContactOf("Anna Kowalska", "anna@example.test"));

        var reader = ReaderOf(store, SyntheticMailUser.Another);

        // Act
        var found = await reader.FindByAddressAsync(
            Address("anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A name two users each wrote down is not ambiguous, because only one of the two books is being read.</summary>
    /// <remarks>
    /// This is the match that decides who a message goes to, so a book that scoped nothing would refuse the send as
    /// ambiguous — or, with one name held once elsewhere, address a person the author never named.
    /// </remarks>
    [Fact]
    public async Task ResolveAsync_ANameEachOfTwoUsersWroteDown_AddressesTheOneInTheCallersOwnBook()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(SyntheticMailUser.Deployment, ContactOf("Anna Kowalska", "anna@example.test"));

        var ours = ContactOf("Anna Kowalska", "anna@work.test");
        store.Hold(SyntheticMailUser.Another, ours);

        var resolver = new NamedRecipientResolver(
            store,
            ContactBookOwnerships.For(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another)));

        // Act
        var resolution = await resolver.ResolveAsync(
            [NamedRecipient.ByContactName(OutgoingRecipientRole.To, ContactDisplayName.Create("Anna Kowalska"))],
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(resolution.Recipients);
        Assert.True(resolution.IsResolved);
        Assert.Equal(ours.Id, recipient.Contact);
        Assert.Equal("anna@work.test", recipient.Address);
    }

    /// <summary>A contact of somebody else's book is unreachable by identity too, so an author naming one addresses nobody.</summary>
    [Fact]
    public async Task ResolveAsync_AContactOfAnotherUsersBook_RefusesTheSendAsNamingSomebodyUnknown()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var theirs = ContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailUser.Deployment, theirs);

        var resolver = new NamedRecipientResolver(
            store,
            ContactBookOwnerships.For(AccessAuthorizations.ForUserGranted(SyntheticMailUser.Another)));

        // Act
        var resolution = await resolver.ResolveAsync(
            [NamedRecipient.ByContact(OutgoingRecipientRole.To, theirs.Id)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(resolution.IsResolved);
        Assert.Equal(RecipientResolutionRefusalReason.ContactUnknown, resolution.Refusal?.Reason);
    }

    /// <summary>Two users writing the same person down is two records, because one address belongs to one contact within one book.</summary>
    /// <remarks>
    /// Uniqueness over the address alone would make the second user's book depend on what the first one had already
    /// written, which is a refusal one user could provoke in another's book by recording an address they share.
    /// </remarks>
    [Fact]
    public async Task RecordAsync_AnAddressAnotherUsersContactHolds_IsWrittenIntoTheCallersOwnBook()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var theirs = ContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailUser.Deployment, theirs);

        var book = BookOf(store, SyntheticMailUser.Another, MailFathomPermission.MailContactsWrite);

        // Act
        var result = await book.RecordAsync(
            NewContactOf("Anna Kowalska", "anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.NotEqual(theirs.Id, result.Contact?.Id);
        Assert.Equal(theirs, Assert.Single(store.ContactsOf(SyntheticMailUser.Deployment)));
        Assert.Equal(result.Contact?.Id, Assert.Single(store.ContactsOf(SyntheticMailUser.Another)).Id);
    }

    /// <summary>Erasing a contact of somebody else's book erases nothing, and reads as a book that never held them.</summary>
    [Fact]
    public async Task EraseAsync_AContactOfAnotherUsersBook_ErasesNothingAndReportsItWasNotHeld()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var theirs = ContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailUser.Deployment, theirs);

        var book = BookOf(store, SyntheticMailUser.Another, MailFathomPermission.AdminErase);

        // Act
        var erasure = await book.EraseAsync(theirs.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(erasure.WasHeld);
        Assert.Equal(theirs, Assert.Single(store.ContactsOf(SyntheticMailUser.Deployment)));
    }

    /// <summary>Giving up on collection gives up on one book's collected half, and leaves every other book's alone.</summary>
    /// <remarks>
    /// The one act over the book that deletes a set of rows rather than a row somebody named, which is what makes the
    /// user predicate load-bearing here in a way it is not elsewhere: losing it would turn one user switching
    /// collection off into an erasure of everything every other user's mail had been read into, with nothing naming a
    /// row for the failure to be about.
    /// </remarks>
    [Fact]
    public async Task EraseCollectedAsync_ACollectedContactInEachOfTwoBooks_ErasesTheCallersOwnAndLeavesTheOther()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var theirs = CollectedContactOf("Anna Kowalska", "anna@example.test");
        var mine = CollectedContactOf("Piotr Nowak", "piotr@example.test");
        store.Hold(SyntheticMailUser.Deployment, theirs);
        store.Hold(SyntheticMailUser.Another, mine);

        var book = BookOf(store, SyntheticMailUser.Another, MailFathomPermission.AdminErase);

        // Act
        var erasure = await book.EraseCollectedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, erasure.ContactsErased);
        Assert.Empty(store.ContactsOf(SyntheticMailUser.Another));
        Assert.Equal(theirs, Assert.Single(store.ContactsOf(SyntheticMailUser.Deployment)));
    }

    private static ContactBookReader ReaderOf(InMemoryContactBookStore store, MailUserId user)
    {
        var authorization = AccessAuthorizations.ForUserGranted(user, MailFathomPermission.MailContactsRead);

        return new ContactBookReader(store, ContactBookOwnerships.For(authorization), authorization);
    }

    private static ContactBook BookOf(
        InMemoryContactBookStore store,
        MailUserId user,
        params MailFathomPermission[] grantedPermissions)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(Now);
        var authorization = AccessAuthorizations.ForUserGranted(user, grantedPermissions);

        return new ContactBook(
            store,
            store,
            ContactBookOwnerships.For(authorization),
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
            timeProvider,
            authorization);
    }

    private static NewContact NewContactOf(string displayName, string address) =>
        new()
        {
            DisplayName = ContactDisplayName.Create(displayName),
            Addresses = [Address(address)],
            PreferredAddress = Address(address),
            Origin = ContactOrigin.Asserted,
        };

    private static Contact ContactOf(string displayName, string address) =>
        ContactOf(displayName, address, ContactOrigin.Asserted);

    private static Contact CollectedContactOf(string displayName, string address) =>
        ContactOf(displayName, address, ContactOrigin.Collected);

    private static Contact ContactOf(string displayName, string address, ContactOrigin origin) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [Address(address)],
            Address(address),
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

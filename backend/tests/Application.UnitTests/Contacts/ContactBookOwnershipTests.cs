// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
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

/// <summary>Covers which books one caller reads and which one their writes go into, over every way a use case reaches them.</summary>
/// <remarks>
/// Each test arranges more than one book and reaches one caller, because a scope is only observable where there is
/// something outside it to leak: a suite holding one user's contacts would pass identically against a read that scoped
/// nothing. The books are the real in-memory ones rather than a substitute, so what is asserted is which books the use
/// case asked for and what books keyed that way answer, rather than an answer a test arranged.
/// </remarks>
public sealed class ContactBookOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A listing is one user's own books, so nobody else's correspondents are served with it.</summary>
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

    /// <summary>A mailbox two people are assigned holds one collected record, and each of them reads that one record.</summary>
    /// <remarks>
    /// The whole point of the collected book belonging to the account: the day an administrator assigns a second user,
    /// nothing is copied and nothing is collected again — the record that was already there is in both of their scopes.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AnAccountTwoUsersShare_ServesTheOneCollectedRecordToEachOfThem()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment);

        var collected = CollectedContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailAccount.Deployment, collected);

        // Act
        var first = await ReaderOf(store, SyntheticMailUser.Deployment, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);
        var second = await ReaderOf(store, SyntheticMailUser.Another, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(collected.Id, Assert.Single(first.Contacts).Id);
        Assert.Equal(collected.Id, Assert.Single(second.Contacts).Id);
        Assert.Equal(1, store.ContactCount);
    }

    /// <summary>A contact the user wrote down hides the collected record carrying the same address, and hides it for that user alone.</summary>
    /// <remarks>
    /// Their own name and their own note are what they see, which is the point of writing somebody down. The other user
    /// of that mailbox never wrote anything, so what they read is unchanged — which is what makes this a precedence
    /// between books rather than an edit to the record both of them read.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AUsersOwnContactCarryingACollectedAddress_HidesTheCollectedOneForThatUserAlone()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment);

        var collected = CollectedContactOf("anna@example.test", "anna@example.test");
        var written = ContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailAccount.Deployment, collected);
        store.Hold(SyntheticMailUser.Deployment, written);

        // Act
        var wroteItDown = await ReaderOf(store, SyntheticMailUser.Deployment, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);
        var didNot = await ReaderOf(store, SyntheticMailUser.Another, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(written.Id, Assert.Single(wroteItDown.Contacts).Id);
        Assert.Equal(collected.Id, Assert.Single(didNot.Contacts).Id);
    }

    /// <summary>An address two of a user's mailboxes both collected is answered once, from the account the order names first.</summary>
    /// <remarks>
    /// The order is the accounts' own identifiers rather than anything either user's record declared, so the same
    /// person is served the same record on every read and two users of one pair of mailboxes are served the same one.
    /// </remarks>
    [Fact]
    public async Task ReadPageAsync_AnAddressCollectedOnTwoOfTheirAccounts_IsAnsweredOnceFromTheFirstOfThem()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var assignments = new StubMailAccountAssignments().Assigning(
            SyntheticMailUser.Deployment,
            SyntheticMailAccount.Another,
            SyntheticMailAccount.Deployment);

        var onTheFirst = CollectedContactOf("Anna Kowalska", "anna@example.test");
        var onTheSecond = CollectedContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailAccount.Deployment, onTheFirst);
        store.Hold(SyntheticMailAccount.Another, onTheSecond);

        // Act
        var page = await ReaderOf(store, SyntheticMailUser.Deployment, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(onTheFirst.Id, Assert.Single(page.Contacts).Id);
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
            SyntheticMailUser.Another,
            NewContactOf("Anna Kowalska", "anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);
        Assert.NotEqual(theirs.Id, result.Contact?.Id);
        Assert.Equal(theirs, Assert.Single(store.ContactsOf(SyntheticMailUser.Deployment)));
        Assert.Equal(result.Contact?.Id, Assert.Single(store.ContactsOf(SyntheticMailUser.Another)).Id);
    }

    /// <summary>Collection writes into the account's book alone, whichever users happen to be assigned it.</summary>
    [Fact]
    public async Task CollectAsync_AnAccountTwoUsersShare_WritesOneRecordIntoTheAccountsBook()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var book = CollectingBookOf(store);

        // Act
        var collected = await book.CollectAsync(
            SyntheticMailAccount.Deployment,
            NewContactOf("Anna Kowalska", "anna@example.test") with { Origin = ContactOrigin.Collected },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, collected.Outcome);
        Assert.Equal(1, store.ContactCount);
        Assert.Equal(collected.Contact?.Id, Assert.Single(store.ContactsOf(SyntheticMailAccount.Deployment)).Id);
        Assert.Empty(store.ContactsOf(SyntheticMailUser.Deployment));
        Assert.Empty(store.ContactsOf(SyntheticMailUser.Another));
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
        var erasure = await book.EraseAsync(
            ContactBookScope.OfOwnBookAlone(SyntheticMailUser.Another),
            theirs.Id,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(erasure.WasHeld);
        Assert.Equal(theirs, Assert.Single(store.ContactsOf(SyntheticMailUser.Deployment)));
    }

    /// <summary>Erasing a collected record takes it out of the account's book, which is to say for every user assigned it.</summary>
    /// <remarks>
    /// The record was one record rather than a copy each, so a data-subject erasure that left it standing for the other
    /// users of that mailbox would not be an erasure at all.
    /// </remarks>
    [Fact]
    public async Task EraseAsync_ACollectedContactOfASharedAccount_TakesItOutForEveryAssignedUser()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticMailUser.Deployment, SyntheticMailAccount.Deployment)
            .Assigning(SyntheticMailUser.Another, SyntheticMailAccount.Deployment);

        var collected = CollectedContactOf("Anna Kowalska", "anna@example.test");
        store.Hold(SyntheticMailAccount.Deployment, collected);

        var book = BookOf(store, SyntheticMailUser.Deployment, MailFathomPermission.AdminErase);

        // Act
        var erasure = await book.EraseAsync(
            new ContactBookScopes(assignments).Of(SyntheticMailUser.Deployment),
            collected.Id,
            TestContext.Current.CancellationToken);
        var theOther = await ReaderOf(store, SyntheticMailUser.Another, assignments)
            .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(erasure.WasHeld);
        Assert.Empty(theOther.Contacts);
    }

    private static ContactBookReader ReaderOf(
        InMemoryContactBookStore store,
        MailUserId user,
        StubMailAccountAssignments? assignments = null)
    {
        var authorization = AccessAuthorizations.ForUserGranted(user, MailFathomPermission.MailContactsRead);

        return new ContactBookReader(
            store,
            ContactBookOwnerships.For(authorization, assignments ?? new StubMailAccountAssignments()),
            authorization);
    }

    private static ContactBook BookOf(
        InMemoryContactBookStore store,
        MailUserId user,
        params MailFathomPermission[] grantedPermissions) =>
        BookOf(store, AccessAuthorizations.ForUserGranted(user, grantedPermissions));

    /// <summary>Builds the book for work MailFathom performs for nobody, which is what collection is reached as.</summary>
    private static ContactBook CollectingBookOf(InMemoryContactBookStore store) =>
        BookOf(store, AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

    private static ContactBook BookOf(InMemoryContactBookStore store, AccessAuthorization authorization)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(Now);

        return new ContactBook(
            store,
            store,
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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Relationship;

/// <summary>Covers the card an opened contact is headed by: what one derivation is scoped to, when none is made at all, and what is scanned on the way back.</summary>
public sealed class ContactRelationshipReaderTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId TheAccount = MailAccountId.Create("work");

    private static readonly MailUserId TheUser = MailUserId.Create(Guid.CreateVersion7());

    /// <summary>A run reads the one contact's own correlation and nothing else, which is what keeps opening one person off everybody else's mail.</summary>
    [Fact]
    public async Task ReadAsync_AContactTheMailKnows_ScopesTheDerivationToThatContactsOwnCorrespondence()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex()
            .WithThreads(ConversationOn(message, "the quarterly review"))
            .WithDocuments(new CorrespondingDocument(message, 1, "review.pdf", "application/pdf", FirstJuly));
        var deriver = new RecordingContactRelationshipDeriver();
        var reader = ReaderOver(index, deriver);

        // Act
        await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        var brief = Assert.Single(deriver.Briefs);

        Assert.Equal("the quarterly review", Assert.Single(brief.Correspondence.Threads).Subject);
        Assert.Equal("review.pdf", Assert.Single(brief.Correspondence.Documents).FileName);
    }

    /// <summary>A person the readable mail says nothing about costs no provider call, there being nothing for a card to rest on.</summary>
    [Fact]
    public async Task ReadAsync_AContactTheMailSaysNothingAbout_DerivesNothingWithoutReachingTheProvider()
    {
        // Arrange
        var deriver = new RecordingContactRelationshipDeriver(DerivedCard());
        var reader = ReaderOver(new InMemoryContactCorrespondenceIndex(), deriver);

        // Act
        var card = await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(card.WasDerived);
        Assert.Empty(deriver.Briefs);
    }

    /// <summary>What the derivation answered is what the caller reads, note, next action and observations together.</summary>
    [Fact]
    public async Task ReadAsync_ADerivationThatProducedACard_PublishesTheNoteTheNextActionAndTheObservations()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex().WithThreads(ConversationOn(message, "the addendum"));
        var reader = ReaderOver(index, new RecordingContactRelationshipDeriver(DerivedCard()));

        // Act
        var card = await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(card.WasDerived);
        Assert.Equal("They lead the addendum.", card.Note?.Text);
        Assert.Equal("Answer the open question.", card.NextAction?.Text);
        Assert.Equal(
            ContactRelationshipAspect.OpenItem,
            Assert.Single(card.Observations).Aspect);
    }

    /// <summary>A card is composed out of somebody's mail and carries whatever it carried, so a deployment that scans redacts every statement of it.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentThatScans_RedactsEveryStatementTheCardPublishes()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex().WithThreads(ConversationOn(message, "the addendum"));
        var reader = ReaderOver(
            index,
            new RecordingContactRelationshipDeriver(DerivedCard($"They pasted {Marker} into the thread.")),
            egressGuard: egress.Guard);

        // Act
        var card = await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, card.Note?.Text, StringComparison.Ordinal);
    }

    /// <summary>The card is the acting person's own, composed on their opening and read by nobody else, so it comes out in the language they read rather than in a mailbox's.</summary>
    [Fact]
    public async Task ReadAsync_AContactTheMailKnows_DerivesTheCardInTheActingUsersOwnLanguage()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex().WithThreads(ConversationOn(message, "the addendum"));
        var deriver = new RecordingContactRelationshipDeriver();
        var reader = ReaderOver(index, deriver, language: MailUserLanguage.Polish);

        // Act
        await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailUserLanguage.Polish, Assert.Single(deriver.Briefs).Language);
    }

    /// <summary>The grant is the one that puts mail in front of a provider, asked here rather than at the transport, so an entrypoint added later meets the same refusal.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheMailAskGrant_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = ReaderOver(
            new InMemoryContactCorrespondenceIndex(),
            new RecordingContactRelationshipDeriver(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
    }

    private static ContactRelationshipReader ReaderOver(
        InMemoryContactCorrespondenceIndex index,
        IContactRelationshipDeriver deriver,
        SensitiveContentEgressGuard? egressGuard = null,
        AccessAuthorization? authorization = null,
        MailUserLanguage language = MailUserLanguage.English)
    {
        var guard = egressGuard ?? SensitiveContentEgressGuards.Inactive();
        var granted = authorization ?? AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailAsk,
            MailFathomPermission.MailRead);
        var scopeResolver = new MailboxScopeResolver(
            CatalogServing(TheAccount),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

        return new ContactRelationshipReader(
            new ContactCorrespondenceReader(
                index,
                scopeResolver,
                guard,
                new RecordingMailboxReadTelemetry(),
                granted,
                new FakeTimeProvider(FirstJuly)),
            deriver,
            scopeResolver,
            guard,
            granted,
            LanguagesAnswering(language));
    }

    /// <summary>Composes a card of the shape a derivation produces: a note, a next action, and one observation.</summary>
    private static ContactRelationship DerivedCard(string note = "They lead the addendum.")
    {
        var sources = new[] { ContactRelationshipSource.Conversation(ConversationOn(StoredEmailId.Create(Guid.CreateVersion7()), "the addendum")) };

        return ContactRelationship.Derived(
            ContactRelationshipStatement.Create(note, sources),
            ContactRelationshipStatement.Create("Answer the open question.", sources),
            [
                new ContactRelationshipObservation(
                    ContactRelationshipAspect.OpenItem,
                    ContactRelationshipStatement.Create("Their question about the cap", sources)!),
            ]);
    }

    private static CorrespondingThread ConversationOn(StoredEmailId message, string subject) => new(
        EmailThreadId.Create(Guid.CreateVersion7()),
        message,
        subject,
        FirstJuly);

    /// <summary>Answers that language for the acting user alone, so a card composed for anybody else would read English and fail the assertion.</summary>
    private static IMailUserLanguages LanguagesAnswering(MailUserLanguage language)
    {
        var languages = Substitute.For<IMailUserLanguages>();
        languages.LanguageOf(Arg.Any<MailUserId>()).Returns(MailUserLanguage.English);
        languages.LanguageOf(TheUser).Returns(language);

        return languages;
    }

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.User.Returns(TheUser);
        catalog.AssignedAccounts.Returns(
        [
            .. servedAccountIds
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        return catalog;
    }

    private static Contact ContactOf(params string[] addresses) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(FirstJuly)),
        ContactDisplayName.Create("Anna Kowalska"),
        [.. addresses.Select(Address)],
        Address(addresses[0]),
        note: null,
        ContactOrigin.Asserted,
        FirstJuly,
        FirstJuly);

    private static EmailAddress Address(string address) => EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
        ? emailAddress
        : throw new ArgumentException($"'{address}' is not an address this test can use.", nameof(address));
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the contact relationship route accepts, what it refuses, and what it puts on the wire.</summary>
/// <remarks>
/// The derivation itself — what it is scoped to, what it withholds, and what is scanned on the way back — is covered
/// where it is decided. What is asserted here is the transport: that a deployment deriving nothing says so without
/// reading anything, that a contact no book in this caller's scope holds is a <c>404</c>, and that a card reaches the
/// wire with each statement's citations spelled the way every other citation in this API is.
/// </remarks>
public sealed class ClientContactRelationshipEndpointTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid TheContact = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid TheMessage = new("33333333-3333-3333-3333-333333333333");

    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();

    private readonly IContactCorrespondenceIndex index = Substitute.For<IContactCorrespondenceIndex>();

    private readonly IContactRelationshipDeriver deriver = Substitute.For<IContactRelationshipDeriver>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void ContactRelationshipRoute_IsThePathAClientComposes() =>
        Assert.Equal(
            "/contacts/{contactId:guid}/relationship",
            ClientContactRelationshipEndpoint.ContactRelationshipRoute);

    /// <summary>A deployment that derives no card says so for every contact, which is what a client reads to know not to draw one.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_ADeploymentThatDerivesNoCard_SaysSoWithoutReadingTheBookOrTheMail()
    {
        // Act
        var result = await ClientContactRelationshipEndpoint.ReadRelationshipAsync(
            TheContact,
            this.Contacts(),
            relationship: null,
            TestContext.Current.CancellationToken);

        // Assert
        var card = Assert.IsType<Ok<ClientContactRelationshipResponse>>(result.Result).Value;

        Assert.NotNull(card);
        Assert.False(card.Derived);
        Assert.Equal(TheContact, card.ContactId);
        Assert.Empty(this.directory.ReceivedCalls());
        Assert.Empty(this.index.ReceivedCalls());
    }

    /// <summary>A derived card arrives whole: the note, what to do next, and every observation under the label it answers.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_AContactWithADerivedCard_AnswersWithTheNoteTheNextActionAndTheObservations()
    {
        // Arrange
        this.Holds(ContactOf("anna@example.test"));
        this.Correlates();
        this.Derives(DerivedCard());

        // Act
        var result = await this.ReadAsync();

        // Assert
        var card = Assert.IsType<Ok<ClientContactRelationshipResponse>>(result.Result).Value;

        Assert.NotNull(card);
        Assert.True(card.Derived);
        Assert.Equal("They lead the addendum.", card.Note?.Text);
        Assert.Equal("Answer the cap question.", card.NextAction?.Text);
        Assert.Equal("OpenItem", Assert.Single(card.Observations).Aspect);
    }

    /// <summary>A conversation is cited as a message and a document as that message's own attachment, which is how a reader follows either one back.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_ACardCitingAConversationAndADocument_SpellsEachAsItsOwnCitationTarget()
    {
        // Arrange
        this.Holds(ContactOf("anna@example.test"));
        this.Correlates();
        this.Derives(DerivedCard());

        // Act
        var result = await this.ReadAsync();

        // Assert
        var card = Assert.IsType<Ok<ClientContactRelationshipResponse>>(result.Result).Value;

        Assert.NotNull(card);

        var conversation = Assert.Single(card.Note!.Sources);
        var document = Assert.Single(card.Observations).Statement.Sources.Single();

        Assert.Equal(EmailCitationTarget.Kind, conversation.Kind);
        Assert.Equal(TheMessage, conversation.Email);
        Assert.Null(conversation.AttachmentPosition);
        Assert.Equal(AttachmentCitationTarget.Kind, document.Kind);
        Assert.Equal(TheMessage, document.Email);
        Assert.Equal(1, document.AttachmentPosition);
    }

    /// <summary>A derivation that produced nothing leaves the contact page as it was rather than failing the read.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_ADerivationThatProducedNothing_AnswersThatNoCardWasDerived()
    {
        // Arrange
        this.Holds(ContactOf("anna@example.test"));
        this.Correlates();
        this.Derives(ContactRelationship.Nothing);

        // Act
        var result = await this.ReadAsync();

        // Assert
        var card = Assert.IsType<Ok<ClientContactRelationshipResponse>>(result.Result).Value;

        Assert.NotNull(card);
        Assert.False(card.Derived);
        Assert.Null(card.Note);
    }

    /// <summary>A contact nobody holds and one this caller may not read answer identically, so neither discloses the other.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_AnIdentifierNamingNoContactThisCallerHolds_IsNotFound()
    {
        // Arrange
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns((Contact?)null);

        // Act
        var result = await this.ReadAsync();

        // Assert
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(this.deriver.ReceivedCalls());
    }

    /// <summary>An identifier naming no contact at all is answered without the book, the mail, or a provider being reached.</summary>
    [Fact]
    public async Task ReadRelationshipAsync_TheEmptyIdentifier_IsNotFoundWithoutReadingAnything()
    {
        // Act
        var result = await this.ReadAsync(Guid.Empty);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(this.directory.ReceivedCalls());
        Assert.Empty(this.deriver.ReceivedCalls());
    }

    private Task<Results<Ok<ClientContactRelationshipResponse>, NotFound>> ReadAsync(Guid? contactId = null) =>
        ClientContactRelationshipEndpoint.ReadRelationshipAsync(
            contactId ?? TheContact,
            this.Contacts(),
            this.Relationship(),
            TestContext.Current.CancellationToken);

    /// <summary>Composes a card of the shape a derivation produces, citing one conversation and one document.</summary>
    private static ContactRelationship DerivedCard()
    {
        var message = StoredEmailId.Create(TheMessage);
        ContactRelationshipSource[] conversation = [new ContactRelationshipSource(message, AttachmentPosition: null)];
        ContactRelationshipSource[] document = [new ContactRelationshipSource(message, AttachmentPosition: 1)];

        return ContactRelationship.Derived(
            ContactRelationshipStatement.Create("They lead the addendum.", conversation),
            ContactRelationshipStatement.Create("Answer the cap question.", conversation),
            [
                new ContactRelationshipObservation(
                    ContactRelationshipAspect.OpenItem,
                    ContactRelationshipStatement.Create("the cap decision", document)!),
            ]);
    }

    /// <summary>States what the derivation answers with, which is what the wire shape is asserted against.</summary>
    private void Derives(ContactRelationship card) =>
        this.deriver
            .DeriveAsync(Arg.Any<ContactRelationshipBrief>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(card));

    /// <summary>States that the mail holds one conversation about this person, which is what carries the read as far as the derivation.</summary>
    private void Correlates()
    {
        this.index.ReadRecentThreadsAsync(
                Arg.Any<MailboxScope>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorrespondingThread>>(
            [
                new CorrespondingThread(
                    EmailThreadId.Create(Guid.CreateVersion7()),
                    StoredEmailId.Create(TheMessage),
                    "the addendum",
                    FirstJuly),
            ]));

        this.index.ReadRecentDocumentsAsync(
                Arg.Any<MailboxScope>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<CorrespondingDocument>>([]));
    }

    private void Holds(Contact contact) =>
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns(contact);

    /// <summary>Builds the book the route reads the contact from, over a substituted directory and a caller granted every half.</summary>
    private ContactBookReader Contacts() => new(
        this.directory,
        new ContactBookOwnership(Granted(), new ContactBookScopes(new StubMailAccountAssignments())),
        Granted());

    /// <summary>Builds the use case the route derives through, over the substituted index and deriver this class arranges.</summary>
    private ContactRelationshipReader Relationship()
    {
        var scopeResolver = new MailboxScopeResolver(
            CatalogServing(MailAccountId.Create("work")),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing);

        return new ContactRelationshipReader(
            new ContactCorrespondenceReader(
                this.index,
                scopeResolver,
                SensitiveContentEgressGuards.Inactive(),
                ReadTelemetry(),
                Granted(),
                TimeProvider.System),
            this.deriver,
            scopeResolver,
            SensitiveContentEgressGuards.Inactive(),
            Granted(),
            LanguagesAnsweringEnglish());
    }

    /// <summary>A telemetry port that opens a scope and reports nothing, which is what a transport test needs of it.</summary>
    private static IMailboxReadTelemetry ReadTelemetry()
    {
        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();

        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        return readTelemetry;
    }

    private static IUserLanguages LanguagesAnsweringEnglish()
    {
        var languages = Substitute.For<IUserLanguages>();

        languages.LanguageOf(Arg.Any<UserId>()).Returns(UserLanguage.English);

        return languages;
    }

    /// <summary>The caller this route is reached by, which holds every grant the answer needs.</summary>
    private static AccessAuthorization Granted() => AccessAuthorizations.ForUserGranted(
        SyntheticUser.Deployment,
        MailFathomPermission.MailAsk,
        MailFathomPermission.MailRead,
        MailFathomPermission.MailContactsRead);

    /// <summary>Reports the accounts this caller is assigned, which is what the correlation narrows by.</summary>
    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();

        catalog.AssignedAccounts.Returns([.. accounts.Select(SyntheticServedAccount.Of)]);

        return catalog;
    }

    private static Contact ContactOf(string address) => Contact.Create(
        ContactId.Create(TheContact),
        ContactDisplayName.Create("Anna Kowalska"),
        [Address(address)],
        Address(address),
        note: null,
        ContactOrigin.Asserted,
        FirstJuly,
        FirstJuly);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress
            : throw new ArgumentException($"'{address}' is not an address this test can use.", nameof(address));
}

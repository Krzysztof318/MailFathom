// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Correspondence;
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

/// <summary>Covers what the contact correlation route accepts, what it refuses, and what it puts on the wire.</summary>
/// <remarks>
/// The correlation itself — its window, its bounds, and the scope it narrows by — is covered where it is decided. What
/// is asserted here is the transport: that a contact no book in this caller's scope holds is a <c>404</c> rather than
/// an empty document, that a contact the mail says nothing about is an empty document rather than a <c>404</c>, and
/// that both lists reach the wire in the shape the routes beside this one are asked with.
/// </remarks>
public sealed class ClientContactCorrespondenceEndpointTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid TheContact = new("11111111-1111-1111-1111-111111111111");

    private static readonly Guid TheConversation = new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid TheMessage = new("33333333-3333-3333-3333-333333333333");

    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();

    private readonly IContactCorrespondenceIndex index = Substitute.For<IContactCorrespondenceIndex>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void ContactCorrespondenceRoute_IsThePathAClientComposes() =>
        Assert.Equal(
            "/contacts/{contactId:guid}/correspondence",
            ClientContactCorrespondenceEndpoint.ContactCorrespondenceRoute);

    /// <summary>A contact the caller holds arrives as the two lists an opened contact is drawn beside.</summary>
    [Fact]
    public async Task ReadCorrespondenceAsync_AContactTheMailKnows_AnswersWithBothLists()
    {
        // Arrange
        this.Holds(ContactOf("anna@example.test"));
        this.Correlates(
            [
                new CorrespondingThread(
                    EmailThreadId.Create(TheConversation),
                    StoredEmailId.Create(TheMessage),
                    "the quarterly review",
                    FirstJuly),
            ],
            [
                new CorrespondingDocument(
                    StoredEmailId.Create(TheMessage),
                    1,
                    "review.pdf",
                    "application/pdf",
                    FirstJuly),
            ]);

        // Act
        var result = await this.ReadAsync();

        // Assert
        var correspondence = Assert.IsType<Ok<ClientContactCorrespondenceResponse>>(result.Result).Value;

        Assert.NotNull(correspondence);
        Assert.Equal(TheContact, correspondence.ContactId);

        var thread = Assert.Single(correspondence.Threads);
        var document = Assert.Single(correspondence.Documents);

        Assert.Equal(TheConversation, thread.ThreadId);
        Assert.Equal(TheMessage, thread.LatestMessageId);
        Assert.Equal("the quarterly review", thread.Subject);
        Assert.Equal(FirstJuly, thread.LastCorrespondedAt);
        Assert.Equal(TheMessage, document.MessageId);
        Assert.Equal(1, document.Position);
        Assert.Equal("review.pdf", document.FileName);
        Assert.Equal("application/pdf", document.MediaType);
    }

    /// <summary>A person in the book the window holds no mail of is an empty answer rather than a missing one.</summary>
    [Fact]
    public async Task ReadCorrespondenceAsync_AContactTheMailSaysNothingAbout_AnswersWithTwoEmptyLists()
    {
        // Arrange
        this.Holds(ContactOf("anna@example.test"));
        this.Correlates([], []);

        // Act
        var result = await this.ReadAsync();

        // Assert
        var correspondence = Assert.IsType<Ok<ClientContactCorrespondenceResponse>>(result.Result).Value;

        Assert.NotNull(correspondence);
        Assert.Empty(correspondence.Threads);
        Assert.Empty(correspondence.Documents);
    }

    /// <summary>A contact nobody holds and one this caller may not read answer identically, so neither discloses the other.</summary>
    [Fact]
    public async Task ReadCorrespondenceAsync_AnIdentifierNamingNoContactThisCallerHolds_IsNotFound()
    {
        // Arrange
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns((Contact?)null);

        // Act
        var result = await this.ReadAsync();

        // Assert
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(this.index.ReceivedCalls());
    }

    /// <summary>An identifier naming no contact at all is answered without the book or the mail being reached.</summary>
    [Fact]
    public async Task ReadCorrespondenceAsync_TheEmptyIdentifier_IsNotFoundWithoutReadingAnything()
    {
        // Act
        var result = await this.ReadAsync(Guid.Empty);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        Assert.Empty(this.directory.ReceivedCalls());
        Assert.Empty(this.index.ReceivedCalls());
    }

    private Task<Results<Ok<ClientContactCorrespondenceResponse>, NotFound>> ReadAsync(Guid? contactId = null) =>
        ClientContactCorrespondenceEndpoint.ReadCorrespondenceAsync(
            contactId ?? TheContact,
            this.Contacts(),
            this.Correspondence(),
            TestContext.Current.CancellationToken);

    /// <summary>States what the index answers the correlation with, which is what the wire shape is asserted against.</summary>
    private void Correlates(
        IReadOnlyList<CorrespondingThread> threads,
        IReadOnlyList<CorrespondingDocument> documents)
    {
        this.index.ReadRecentThreadsAsync(
                Arg.Any<MailboxScope>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(threads));

        this.index.ReadRecentDocumentsAsync(
                Arg.Any<MailboxScope>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(documents));
    }

    private void Holds(Contact contact) =>
        this.directory
            .FindAsync(Arg.Any<ContactBookScope>(), Arg.Any<ContactId>(), Arg.Any<CancellationToken>())
            .Returns(contact);

    /// <summary>Builds the book the route reads the contact from, over a substituted directory and a caller granted both halves.</summary>
    private ContactBookReader Contacts() => new(
        this.directory,
        new ContactBookOwnership(Granted(), new ContactBookScopes(new StubMailAccountAssignments())),
        Granted());

    /// <summary>Builds the correlation the route reads the mail from, over the in-memory index this class arranges.</summary>
    private ContactCorrespondenceReader Correspondence() => new(
        this.index,
        new MailboxScopeResolver(
            CatalogServing(MailAccountId.Create("work")),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        SensitiveContentEgressGuards.Inactive(),
        ReadTelemetry(),
        Granted(),
        TimeProvider.System);

    /// <summary>A telemetry port that opens a scope and reports nothing, which is what a transport test needs of it.</summary>
    private static IMailboxReadTelemetry ReadTelemetry()
    {
        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();

        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        return readTelemetry;
    }

    /// <summary>The caller these routes are reached by, which holds both grants the answer needs.</summary>
    private static AccessAuthorization Granted() => AccessAuthorizations.ForUserGranted(
        SyntheticUser.Deployment,
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

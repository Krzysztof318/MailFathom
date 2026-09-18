// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Observability;
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

namespace MailFathom.Application.UnitTests.Contacts.Correspondence;

/// <summary>Covers the correlation an opened contact is drawn beside: what it asks the index, what it narrows by, and what it publishes.</summary>
public sealed class ContactCorrespondenceReaderTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId TheAccount = MailAccountId.Create("work");

    /// <summary>Every address a contact holds is one the correlation matches on, in the comparison form the index keys on, or a person's second mailbox is invisible.</summary>
    [Fact]
    public async Task ReadAsync_AContactHoldingTwoAddresses_AsksTheIndexAboutBothInTheirComparisonForm()
    {
        // Arrange
        var index = new InMemoryContactCorrespondenceIndex();
        var reader = ReaderOver(index);

        // Act
        await reader.ReadAsync(
            ContactOf("Anna@example.com", "anna.kowalska@example.org"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            index.Reads,
            read => Assert.Equal(["ANNA@EXAMPLE.COM", "ANNA.KOWALSKA@EXAMPLE.ORG"], read.NormalizedAddresses));
    }

    /// <summary>The window is measured back from now, so an opened contact answers about what has been happening rather than about the whole mailbox.</summary>
    [Fact]
    public async Task ReadAsync_AContact_MeasuresTheWindowBackFromTheCurrentInstant()
    {
        // Arrange
        var index = new InMemoryContactCorrespondenceIndex();
        var clock = new FakeTimeProvider(FirstJuly);
        var reader = ReaderOver(index, clock: clock);

        // Act
        await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            index.Reads,
            read => Assert.Equal(
                FirstJuly.AddDays(-ContactCorrespondenceBounds.WindowDays),
                read.CorrespondedOnOrAfter));
    }

    /// <summary>Both lists are the caller's own mail, so the read is narrowed by the scope every other mail read composes.</summary>
    [Fact]
    public async Task ReadAsync_AContact_NarrowsByTheCallersOwnAccountsAndLeavesJunkOut()
    {
        // Arrange
        var index = new InMemoryContactCorrespondenceIndex();
        var reader = ReaderOver(index);

        // Act
        await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        Assert.All(index.Reads, read =>
        {
            Assert.Equal([TheAccount], read.Scope.AccountIds);
            Assert.False(read.Scope.IncludesJunkMail);
        });
    }

    /// <summary>What the index answered is what the caller reads, both lists in the order the index published them.</summary>
    [Fact]
    public async Task ReadAsync_AContactTheMailKnows_PublishesTheConversationsAndTheDocuments()
    {
        // Arrange
        var conversation = EmailThreadId.Create(Guid.CreateVersion7());
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex()
            .WithThreads(new CorrespondingThread(conversation, message, "the quarterly review", FirstJuly))
            .WithDocuments(new CorrespondingDocument(message, 1, "review.pdf", "application/pdf", FirstJuly));
        var reader = ReaderOver(index);

        // Act
        var correspondence = await reader.ReadAsync(
            ContactOf("anna@example.com"),
            TestContext.Current.CancellationToken);

        // Assert
        var thread = Assert.Single(correspondence.Threads);
        var document = Assert.Single(correspondence.Documents);

        Assert.Equal(conversation, thread.ThreadId);
        Assert.Equal("the quarterly review", thread.Subject);
        Assert.Equal("review.pdf", document.FileName);
    }

    /// <summary>A user who owns no account is answered about their own nothing rather than out of everybody else's mail.</summary>
    [Fact]
    public async Task ReadAsync_AUserAssignedNoAccount_AnswersNothingWithoutReachingTheIndex()
    {
        // Arrange
        var index = new InMemoryContactCorrespondenceIndex()
            .WithThreads(new CorrespondingThread(
                EmailThreadId.Create(Guid.CreateVersion7()),
                StoredEmailId.Create(Guid.CreateVersion7()),
                "somebody else's exchange",
                FirstJuly));
        var reader = ReaderOver(index, accountCatalog: CatalogServing());

        // Act
        var correspondence = await reader.ReadAsync(
            ContactOf("anna@example.com"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(correspondence.Threads);
        Assert.Empty(correspondence.Documents);
        Assert.Empty(index.Reads);
    }

    /// <summary>A subject and a file name are both text a sender wrote, so a deployment that scans redacts both before either leaves.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentThatScans_RedactsTheSubjectAndTheFileName()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var index = new InMemoryContactCorrespondenceIndex()
            .WithThreads(new CorrespondingThread(
                EmailThreadId.Create(Guid.CreateVersion7()),
                message,
                $"the key is {Marker}",
                FirstJuly))
            .WithDocuments(new CorrespondingDocument(
                message,
                1,
                $"{Marker}.pdf",
                "application/pdf",
                FirstJuly));
        var reader = ReaderOver(index, egressGuard: egress.Guard);

        // Act
        var correspondence = await reader.ReadAsync(
            ContactOf("anna@example.com"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, Assert.Single(correspondence.Threads).Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, Assert.Single(correspondence.Documents).FileName, StringComparison.Ordinal);
    }

    /// <summary>The read is reported as the operation it is, so a card a screen waited on has a use case above its queries in a trace.</summary>
    [Fact]
    public async Task ReadAsync_ACorrelationThatWasServed_ReportsItAndWhatItReturned()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.CreateVersion7());
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var index = new InMemoryContactCorrespondenceIndex()
            .WithThreads(new CorrespondingThread(
                EmailThreadId.Create(Guid.CreateVersion7()),
                message,
                "the quarterly review",
                FirstJuly))
            .WithDocuments(new CorrespondingDocument(message, 1, "review.pdf", "application/pdf", FirstJuly));
        var reader = ReaderOver(index, readTelemetry: readTelemetry);

        // Act
        await reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.CorrelateContactMail, read.Operation);
        Assert.Equal(2, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint added later meets the same refusal.</summary>
    [Fact]
    public async Task ReadAsync_ACallerWithoutTheMailReadGrant_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var reader = ReaderOver(
            new InMemoryContactCorrespondenceIndex(),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailContactsRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            reader.ReadAsync(ContactOf("anna@example.com"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    private static ContactCorrespondenceReader ReaderOver(
        InMemoryContactCorrespondenceIndex index,
        ICallerMailAccountCatalog? accountCatalog = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null,
        TimeProvider? clock = null) => new(
        index,
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(TheAccount),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        egressGuard ?? SensitiveContentEgressGuards.Inactive(),
        readTelemetry ?? new RecordingMailboxReadTelemetry(),
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
        clock ?? new FakeTimeProvider(FirstJuly));

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
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

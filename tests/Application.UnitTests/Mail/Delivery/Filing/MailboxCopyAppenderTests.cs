// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Filing;

/// <summary>Covers the one append every copy of a message MailFathom composed is filed through.</summary>
/// <remarks>
/// What is written about here is the order of the two writes around the command and what each way of ending is
/// reported as, because those are what a second filing path would otherwise restate and get subtly wrong. Which row
/// each write moves belongs to the filer that supplied it and is covered where that filer is.
/// </remarks>
public sealed class MailboxCopyAppenderTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private static readonly OutgoingEmailId Send = OutgoingEmailId.Create(Guid.NewGuid());

    private static readonly MailDraftId Draft = MailDraftId.Create(Guid.NewGuid());

    private readonly FakeTimeProvider clock = new(Moment);
    private readonly StubMailFolderMappings mappings = StubMailFolderMappings.Nothing;
    private readonly InMemoryMailFolderResolutionStore folderResolutions = new();
    private readonly IEmailContentStore contentStore = Substitute.For<IEmailContentStore>();
    private readonly IMailboxWriteSession writeSession = Substitute.For<IMailboxWriteSession>();
    private readonly IMailboxWriteSessionFactory writeSessions = Substitute.For<IMailboxWriteSessionFactory>();

    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicies =
        Substitute.For<IMailTransportSecurityPolicyReader>();

    private readonly List<string> steps = [];

    /// <summary>Gets the binding the caller was told to write its issued record against.</summary>
    private MailFolderResolution? IssuedInto { get; set; }

    /// <summary>A role no folder of the account plays leaves nothing asked of the mail server and nothing written.</summary>
    [Fact]
    public async Task AppendAsync_ARoleNoFolderPlays_ReachesNoServerAndIssuesNoRecord()
    {
        // Arrange
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(appender, MailboxCopySource.OutgoingEmail(Send));

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.DestinationUnavailable, appended.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable, appended.Failure);
        Assert.Null(appended.Copy);
        Assert.Empty(this.steps);
        await this.writeSessions.DidNotReceiveWithAnyArgs().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A record and its message are written in one transaction, so a source with no appendable message describes a copy
    /// that can never exist rather than a message still being stored. Nothing is issued, and no later attempt can
    /// invent the bytes.
    /// </summary>
    /// <param name="storesAnEmptyMessage">Whether the store holds a message of no bytes rather than none at all.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AppendAsync_ASourceWithNoAppendableMessage_ReachesNoServerAndIssuesNoRecord(
        bool storesAnEmptyMessage)
    {
        // Arrange
        this.MapSentFolder();
        this.contentStore
            .FindOutgoingContentAsync(Send, Arg.Any<CancellationToken>())
            .Returns(storesAnEmptyMessage ? Stored(ReadOnlyMemory<byte>.Empty) : null);
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(appender, MailboxCopySource.OutgoingEmail(Send));

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.MessageUnavailable, appended.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly, appended.Failure);
        Assert.Empty(this.steps);
        await this.writeSessions.DidNotReceiveWithAnyArgs().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The issued record is durable before the command goes out and the confirmation is written after the server
    /// answered. That order is the whole of the safety here: an <c>APPEND</c> issued twice is a second message in
    /// somebody's folder, and a row found at the issued stage is what stops the second one.
    /// </summary>
    [Fact]
    public async Task AppendAsync_AServerThatAcceptsTheCopy_RecordsTheAppendIssuedBeforeItAndConfirmedAfterIt()
    {
        // Arrange
        var binding = this.MapSentFolder();
        this.StoreOutgoingMessage();
        var placed = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(7), ImapUid.Create(31)),
            InternetMessageId: null);
        this.AnswerAppendWith(placed);
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(appender, MailboxCopySource.OutgoingEmail(Send));

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.Appended, appended.Outcome);
        Assert.Same(placed, appended.Copy);
        Assert.Null(appended.Failure);
        Assert.Equal(["issued", "append", "confirmed"], this.steps);
        Assert.Equal(binding, this.IssuedInto);
        await this.writeSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            OutgoingMailFiling.Sent.Flags,
            Moment,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A draft's copy is read from the draft's own stored revision rather than from any outgoing record.</summary>
    [Fact]
    public async Task AppendAsync_ADraftSource_AppendsTheRevisionTheDraftIsHeldAs()
    {
        // Arrange
        this.MapDraftsFolder();
        this.contentStore
            .FindMailDraftContentAsync(Draft, Arg.Any<CancellationToken>())
            .Returns(Stored(Encoding.ASCII.GetBytes("Subject: a revision\r\n\r\nbody")));
        this.AnswerAppendWith(new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(1), ImapUid.Create(4)),
            InternetMessageId: null));
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(
            appender,
            MailboxCopySource.MailDraft(Draft),
            OutgoingMailFiling.Draft);

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.Appended, appended.Outcome);
        await this.contentStore.DidNotReceiveWithAnyArgs().FindOutgoingContentAsync(
            Arg.Any<OutgoingEmailId>(),
            Arg.Any<CancellationToken>());
        await this.writeSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            OutgoingMailFiling.Draft.Flags,
            Moment,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An append the server never answered leaves nobody able to say whether the copy is in the folder, so it is
    /// reported as an outcome nothing can settle rather than raised into a retry that would file a second copy. A
    /// first-party failure keeps the code an operator looks it up by.
    /// </summary>
    [Fact]
    public async Task AppendAsync_AnAppendTheServerNeverAnswered_ReportsAnOutcomeNobodyCanSettle()
    {
        // Arrange
        this.MapSentFolder();
        this.StoreOutgoingMessage();
        this.writeSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(
                Account,
                MailFolderAlias.Create("sent"),
                new TimeoutException("The append was issued and the server never answered.")));
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(appender, MailboxCopySource.OutgoingEmail(Send));

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.OutcomeUnknown, appended.Outcome);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, appended.Failure);
        Assert.Null(appended.Copy);

        // The issued record is already durable, which is what a later pass reads to know a copy may be in the folder.
        Assert.Equal(["issued"], this.steps);
    }

    /// <summary>
    /// A confirmation that could not be written is the same ambiguity as an append that was never answered: the copy is
    /// already in somebody's folder and the record does not say so. Reporting it as an ordinary failure would let the
    /// copy be filed a second time.
    /// </summary>
    [Fact]
    public async Task AppendAsync_AConfirmationThatCouldNotBeWritten_ReportsAnOutcomeNobodyCanSettle()
    {
        // Arrange
        this.MapSentFolder();
        this.StoreOutgoingMessage();
        this.AnswerAppendWith(new AppendedMailCopy(RemoteEmailPlacement.NotReported(), InternetMessageId: null));
        var appender = this.Appender();

        // Act
        var appended = await appender.AppendAsync(
            Account,
            OutgoingMailFiling.Sent,
            MailboxCopySource.OutgoingEmail(Send),
            this.RecordIssuedAsync,
            _ => throw new PersistenceConcurrencyConflictException("every attempt conflicted"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.OutcomeUnknown, appended.Outcome);
        Assert.Equal(MailFathomErrorCode.PersistenceConcurrencyConflict, appended.Failure);
        Assert.Equal(["issued", "append"], this.steps);
    }

    /// <summary>A failure carrying no code of its own says it is unaccounted for rather than borrowing one that would mislead.</summary>
    [Fact]
    public async Task AppendAsync_AFailureCarryingNoErrorCode_NamesItUnaccountedFor()
    {
        // Arrange
        this.MapSentFolder();
        this.StoreOutgoingMessage();
        this.writeSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("something nobody classified"));
        var appender = this.Appender();

        // Act
        var appended = await this.AppendAsync(appender, MailboxCopySource.OutgoingEmail(Send));

        // Assert
        Assert.Equal(MailboxCopyAppendOutcome.OutcomeUnknown, appended.Outcome);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingFailedUnexpectedly, appended.Failure);
    }

    /// <summary>The unusable struct default names no place for the copy to go, so it is refused rather than resolved.</summary>
    [Fact]
    public async Task AppendAsync_TheUnspecifiedFiling_IsRefused()
    {
        // Arrange
        var appender = this.Appender();

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => appender.AppendAsync(
            Account,
            default,
            MailboxCopySource.OutgoingEmail(Send),
            this.RecordIssuedAsync,
            this.RecordConfirmedAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("filing", refusal.ParamName);
    }

    /// <summary>Every collaborator the append needs from its caller is required.</summary>
    [Fact]
    public async Task AppendAsync_ACallerSuppliedCollaboratorThatIsNull_IsRefused()
    {
        // Arrange
        var appender = this.Appender();

        // Act
        var missingSource = await Assert.ThrowsAsync<ArgumentNullException>(() => appender.AppendAsync(
            Account,
            OutgoingMailFiling.Sent,
            null!,
            this.RecordIssuedAsync,
            this.RecordConfirmedAsync,
            TestContext.Current.CancellationToken));
        var missingIssuedWrite = await Assert.ThrowsAsync<ArgumentNullException>(() => appender.AppendAsync(
            Account,
            OutgoingMailFiling.Sent,
            MailboxCopySource.OutgoingEmail(Send),
            null!,
            this.RecordConfirmedAsync,
            TestContext.Current.CancellationToken));
        var missingConfirmation = await Assert.ThrowsAsync<ArgumentNullException>(() => appender.AppendAsync(
            Account,
            OutgoingMailFiling.Sent,
            MailboxCopySource.OutgoingEmail(Send),
            this.RecordIssuedAsync,
            null!,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            ["source", "recordIssuedAsync", "recordConfirmedAsync"],
            new[] { missingSource, missingIssuedWrite, missingConfirmation }.Select(refusal => refusal.ParamName));
    }

    /// <summary>Reading a source's bytes needs the port they are held behind.</summary>
    [Fact]
    public async Task FindContentAsync_NoContentStore_IsRefused() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            MailboxCopySource.OutgoingEmail(Send).FindContentAsync(null!, CancellationToken.None));

    /// <summary>A copy is reported with what the server said about it, which a result cannot be built without.</summary>
    [Fact]
    public void Appended_NoCopy_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => MailboxCopyAppendResult.Appended(null!));

    /// <summary>Nothing about what ended an attempt can be read from an absent exception.</summary>
    [Fact]
    public void FailureCodeOf_NoFailure_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => MailboxCopyAppender.FailureCodeOf(null!));

    private static StoredEmailContent Stored(ReadOnlyMemory<byte> rawMime) =>
        new(rawMime, rawMime.Length, SHA256.HashData(rawMime.Span));

    private MailboxCopyAppender Appender()
    {
        this.transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(RequiredTlsPolicy);
        this.writeSessions
            .OpenForWritingAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => this.writeSession);

        var persistenceSessions = Substitute.For<IPersistenceSessionFactory>();

        var destinations = new MailboxDestinationResolver(
            this.mappings.Resolver,
            this.folderResolutions,
            new MailFolderResolver(
                Substitute.For<IRemoteFolderCatalog>(),
                Substitute.For<IRemoteFolderCreator>(),
                this.folderResolutions,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                persistenceSessions,
                this.clock),
            this.transportSecurityPolicies);

        return new MailboxCopyAppender(
            this.writeSessions,
            destinations,
            this.contentStore,
            this.transportSecurityPolicies,
            this.clock);
    }

    private Task<MailboxCopyAppendResult> AppendAsync(
        MailboxCopyAppender appender,
        MailboxCopySource source,
        OutgoingMailFiling? filing = null) =>
        appender.AppendAsync(
            Account,
            filing ?? OutgoingMailFiling.Sent,
            source,
            this.RecordIssuedAsync,
            this.RecordConfirmedAsync,
            TestContext.Current.CancellationToken);

    private Task RecordIssuedAsync(MailFolderResolution binding, CancellationToken cancellationToken)
    {
        this.IssuedInto = binding;
        this.steps.Add("issued");

        return Task.CompletedTask;
    }

    private Task RecordConfirmedAsync(AppendedMailCopy copy)
    {
        this.steps.Add("confirmed");

        return Task.CompletedTask;
    }

    private MailFolderResolution MapSentFolder() => this.Map(MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");

    private MailFolderResolution MapDraftsFolder() => this.Map(MailFolderSpecialUse.Drafts, "drafts", "INBOX.Drafts");

    private MailFolderResolution Map(MailFolderSpecialUse role, string alias, string remotePath)
    {
        var folderAlias = MailFolderAlias.Create(alias);

        this.mappings.With(
            Account,
            MailFolderMapping.ToRemotePath(
                folderAlias,
                RemoteFolderPath.Create(remotePath),
                MailFolderParticipation.Full,
                mayCreateMissingFolder: false,
                role));

        return this.folderResolutions.Bind(Account, folderAlias, remotePath);
    }

    private void StoreOutgoingMessage() =>
        this.contentStore
            .FindOutgoingContentAsync(Send, Arg.Any<CancellationToken>())
            .Returns(Stored(Encoding.ASCII.GetBytes("Subject: a send\r\n\r\nbody")));

    private void AnswerAppendWith(AppendedMailCopy copy) =>
        this.writeSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                this.steps.Add("append");

                return Task.FromResult(copy);
            });
}

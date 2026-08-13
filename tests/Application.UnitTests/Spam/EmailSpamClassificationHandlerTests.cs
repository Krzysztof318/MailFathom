// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

/// <summary>Covers what one leased classification does, and what makes running it twice the same as running it once.</summary>
public sealed class EmailSpamClassificationHandlerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly EmailOccurrenceId Occurrence = EmailOccurrenceId.Create(
        Account,
        new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(9),
        ImapUid.Create(4401));

    private static readonly MailTransportSecurityPolicy TlsOnConnect = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    private readonly InMemoryClassifiableEmailReader emails = new();

    private readonly InMemoryEmailSpamClassificationStore classifications = new();

    private readonly InMemoryMailboxMutationRecordStore mutations = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly IEmailContentStore contentStore = Substitute.For<IEmailContentStore>();

    private readonly FakeTimeProvider timeProvider = new(EvaluatedAt);

    public EmailSpamClassificationHandlerTests() => this.contentStore
        .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
        .Returns(_ => SomeContent());

    [Fact]
    public void JobType_Always_IsTheClassificationOfOneOccurrence() =>
        Assert.Equal(JobType.ClassifyEmailSpam, this.CreateHandler().JobType);

    [Fact]
    public async Task RunAsync_AnOccurrenceNobodyHasScored_RecordsTheVerdictAndActsOnIt()
    {
        // Arrange
        var emailId = this.StoreEmailAtTheOccurrence();

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            EmailOccurrenceJobPayload.For(Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([emailId], this.classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(SpamVerdict.Spam, this.classifications.Saved.Single().Verdict);
        Assert.Equal(1, this.mutations.OpenedRecordCount);
    }

    /// <summary>An attempt that committed its verdict and lost its lease leaves the next one the filing to finish.</summary>
    [Fact]
    public async Task RunAsync_AnOccurrenceAnEarlierAttemptAlreadyScored_ScoresNothingAgainAndStillActsOnTheVerdict()
    {
        // Arrange
        var emailId = this.StoreEmailAtTheOccurrence();
        this.classifications.Hold(ClassificationOf(emailId));

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            EmailOccurrenceJobPayload.For(Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(1, this.mutations.OpenedRecordCount);
    }

    /// <summary>Mail expunged between the enqueue and the lease is the message leaving, not work to attempt again.</summary>
    [Fact]
    public async Task RunAsync_AnOccurrenceNothingIsStoredAt_EndsTheJobWithoutClassifyingOrActing()
    {
        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            EmailOccurrenceJobPayload.For(Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(0, this.mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ClassificationSwitchedOff_RecordsNoVerdictAndAsksTheMailboxForNothing()
    {
        // Arrange
        this.StoreEmailAtTheOccurrence();

        // Act
        await this.CreateHandler(MarksJunkRead, SpamClassificationSettings.Disabled).RunAsync(
            EmailOccurrenceJobPayload.For(Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(0, this.mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefusedAsTheWrongWork()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => this.CreateHandler().RunAsync(
            MailAccountJobPayload.For(Account),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("payload", refusal.ParamName);
    }

    private static SpamActionSettings MarksJunkRead =>
        SpamActionSettings.Create(filesJunk: false, marksJunkRead: true, threshold: null);

    private static SpamClassificationSettings SettingsCovering(params MailFolderAlias[] aliases) =>
        SpamClassificationSettings.Create(isEnabled: true, usesScanner: false, aliases);

    /// <summary>A record an earlier attempt would have left, under the terms this test's settings name.</summary>
    private static SpamClassification ClassificationOf(StoredEmailId emailId) => SpamClassification.Create(
        emailId,
        SpamVerdict.Spam,
        SpamClassificationStage.Deterministic,
        assessment: null,
        corpusRevision: null,
        SettingsCovering(Inbox).Profile,
        [],
        EvaluatedAt.AddMinutes(-1));

    /// <summary>Builds content the handler only hands to its collaborators, so the bytes themselves say nothing.</summary>
    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA body nothing here reads.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    /// <summary>Answers with the classified occurrence for whichever message the recorder asks about.</summary>
    private static ISpamActionOccurrenceReader OccurrenceReader()
    {
        var reader = Substitute.For<ISpamActionOccurrenceReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => new SpamActionOccurrence(
                call.Arg<StoredEmailId>(),
                Occurrence,
                Inbox,
                IsRemotelySeen: false));

        return reader;
    }

    private StoredEmailId StoreEmailAtTheOccurrence()
    {
        var emailId = this.emails.Add(new ClassifiableEmail(
            StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
            Account,
            Inbox));

        this.emails.AddOccurrence(Occurrence, emailId);

        return emailId;
    }

    private EmailSpamClassificationHandler CreateHandler(
        SpamActionSettings? actions = null,
        SpamClassificationSettings? settings = null)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.Settings.Returns(settings ?? SettingsCovering(Inbox));

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var commitPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            this.timeProvider);

        return new EmailSpamClassificationHandler(
            this.emails,
            this.classifications,
            this.CreateClassifier(settingsReader, commitPolicy),
            this.CreateActionRecorder(actions ?? SpamActionSettings.None, sessionFactory, commitPolicy));
    }

    /// <summary>Builds the real use case the handler scores through, over a header that says the mail is junk.</summary>
    private EmailSpamClassifier CreateClassifier(
        ISpamClassificationSettingsReader settingsReader,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        var headerReader = Substitute.For<IEmailSpamHeaderReader>();
        headerReader
            .ReadAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(SpamHeaderFacts.Create(
                [],
                [new ProviderSpamHeaderValue("X-Spam-Status", "Yes, score=15.2 required=5.0")]));

        return new EmailSpamClassifier(
            this.emails,
            this.contentStore,
            headerReader,
            StubJunkMailFolderCatalog.None,
            new DeterministicSpamClassifier(),
            settingsReader,
            this.classifications,
            Substitute.For<IEmailChunkStore>(),
            new RecordingDerivedWorkGateTelemetry(),
            commitPolicy,
            this.timeProvider);
    }

    private SpamActionRecorder CreateActionRecorder(
        SpamActionSettings actions,
        IPersistenceSessionFactory sessionFactory,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        var settingsReader = Substitute.For<ISpamActionSettingsReader>();
        settingsReader.Actions.Returns(actions);

        var dispositions = Substitute.For<IAuthoredDeleteEmailDispositionReader>();
        dispositions
            .GetAuthoredDeleteDisposition(Arg.Any<MailAccountId>())
            .Returns(AuthoredDeleteEmailDisposition.RetainLocalCopy);

        return new SpamActionRecorder(
            settingsReader,
            OccurrenceReader(),
            this.mutations,
            this.CreateDestinationResolver(sessionFactory),
            dispositions,
            commitPolicy);
    }

    /// <summary>Resolves destinations over a server advertising nothing, because no test here files anything.</summary>
    private MailboxDestinationResolver CreateDestinationResolver(IPersistenceSessionFactory sessionFactory)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>([]));

        var transportSecurityPolicies = Substitute.For<IMailTransportSecurityPolicyReader>();
        transportSecurityPolicies.GetPolicy(Arg.Any<MailAccountId>()).Returns(TlsOnConnect);

        return new MailboxDestinationResolver(
            StubMailFolderMappings.Nothing.Resolver,
            this.bindings,
            new MailFolderResolver(
                remoteFolderCatalog,
                Substitute.For<IRemoteFolderCreator>(),
                this.bindings,
                Substitute.For<IMailFolderMappingChangeAuditor>(),
                sessionFactory,
                this.timeProvider),
            transportSecurityPolicies);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

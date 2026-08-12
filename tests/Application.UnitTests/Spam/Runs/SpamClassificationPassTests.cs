// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Runs;
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

namespace MailFathom.Application.UnitTests.Spam.Runs;

/// <summary>Covers what a pass carries, what it skips, what it leaves for the next one, and how a run stops early.</summary>
public sealed class SpamClassificationPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailAccountId OtherAccount = MailAccountId.Create("acct-2");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

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

    private readonly InMemorySpamClassificationRunStore runs = new();

    private readonly InMemoryMailboxMutationRecordStore mutations = new();

    private readonly InMemoryMailFolderResolutionStore bindings = new();

    private readonly IEmailContentStore contentStore = Substitute.For<IEmailContentStore>();

    private readonly FakeTimeProvider timeProvider = new(EvaluatedAt);

    private readonly HashSet<StoredEmailId> emailsWithoutContent = [];

    private int storedEmailCount;

    /// <summary>Stores content for every occurrence but the ones a test says nothing is stored for.</summary>
    public SpamClassificationPassTests() => this.contentStore
        .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
        .Returns(call => this.emailsWithoutContent.Contains(call.Arg<StoredEmailId>()) ? null : SomeContent());

    [Fact]
    public async Task RunAsync_NoRunOutstanding_ReportsNoneAndReadsNoMail()
    {
        // Arrange
        this.StoreEmail();
        var pass = this.CreatePass();

        // Act
        var report = await pass.RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(report.Walk);
        Assert.Null(report.Ending);
        Assert.Empty(this.emails.RequestedBatchSizes);
        Assert.Empty(this.runs.Saves);
    }

    [Fact]
    public async Task RunAsync_ARequestedRun_ClassifiesTheScopeAndCompletes()
    {
        // Arrange
        this.StoreEmail();
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(2, report.Walk?.SpamEmailCount);
        Assert.False(report.Walk?.EmailsRemain);
        Assert.Equal(SpamClassificationRunEnding.Completed, report.Ending);
        Assert.Equal(SpamClassificationRunEnding.Completed, this.runs.Find(Account)?.Ending);
        Assert.Equal(EvaluatedAt, this.runs.Find(Account)?.EndedAt);
        Assert.Equal(2, this.classifications.Saved.Count);
    }

    /// <summary>The run binds what it is decided under, so a later pass can tell a moved threshold from an unchanged one.</summary>
    [Fact]
    public async Task RunAsync_APassPickingTheRunUp_BindsTheProfileItStartedUnder()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SettingsCovering(Inbox).Profile, report.Profile);
        Assert.Equal(SettingsCovering(Inbox).Profile, this.runs.Find(Account)?.Profile);
    }

    /// <summary>Skipping the scoring is the saving; skipping the action would make switching filing on file nothing.</summary>
    [Fact]
    public async Task RunAsync_MailAlreadyDecidedUnderTheRunsProfile_SkipsTheScoringAndStillActsOnIt()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(SpamActionSettings.Create(
            filesJunk: false,
            marksJunkRead: true,
            threshold: null)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.SkippedEmailCount);
        Assert.Equal(0, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(1, report.Walk?.SpamEmailCount);
        Assert.Equal(1, report.Walk?.ActedEmailCount);
        Assert.Empty(this.classifications.Saved);
        Assert.Equal(1, this.mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ARunThatRescores_ScoresMailAlreadyDecidedUnderItsProfile()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
        this.runs.Arrange(this.RequestedRun(rescores: true));

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(0, report.Walk?.SkippedEmailCount);
        Assert.Single(this.classifications.Saved);
    }

    /// <summary>A record from before the profile was part of one names terms the run cannot compare, so it is reached again.</summary>
    [Fact]
    public async Task RunAsync_ARecordNamingNoProfile_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.classifications.Hold(ClassificationOf(stored, profile: default));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_ARecordDecidedUnderOtherTerms_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.classifications.Hold(
            ClassificationOf(stored, SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 3)));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.classifications.Saved);
    }

    /// <summary>A run cannot finish under terms it did not start with, and half a mailbox decided both ways is worse.</summary>
    [Fact]
    public async Task RunAsync_TheProfileMovedWhileARunWasOutstanding_EndsItAsSupersededWithoutClassifying()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun() with
        {
            Profile = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 3),
        });

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationRunEnding.Superseded, report.Ending);
        Assert.Equal(SpamClassificationRunEnding.Superseded, this.runs.Find(Account)?.Ending);
        Assert.Empty(this.classifications.Saved);
        Assert.Empty(this.emails.RequestedBatchSizes);
    }

    [Fact]
    public async Task RunAsync_ClassificationSwitchedOffWhileARunWasOutstanding_EndsItAsDisabled()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(settings: SpamClassificationSettings.Disabled)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationRunEnding.Disabled, report.Ending);
        Assert.Equal(SpamClassificationRunEnding.Disabled, this.runs.Find(Account)?.Ending);
        Assert.Empty(this.classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_MoreMailThanThePassBudgetReaches_CommitsWhatItReachedAndLeavesTheRest()
    {
        // Arrange
        var stored = Enumerable.Range(0, 5).Select(_ => this.StoreEmail()).ToArray();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass(batchSize: 1, maxBatchesPerPass: 2)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.True(report.Walk?.EmailsRemain);
        Assert.Null(report.Ending);
        Assert.Equal(stored[1], this.runs.Find(Account)?.Position);
        Assert.True(this.runs.Find(Account)?.IsOutstanding);
    }

    [Fact]
    public async Task RunAsync_ARunLeftPartWayThrough_ResumesFromTheCommittedPosition()
    {
        // Arrange
        var stored = Enumerable.Range(0, 3).Select(_ => this.StoreEmail()).ToArray();
        this.runs.Arrange(this.RequestedRun() with
        {
            Profile = SettingsCovering(Inbox).Profile,
            Position = stored[0],
            ClassifiedEmailCount = 1,
        });

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [stored[1], stored[2]],
            this.classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(2, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(3, this.runs.Find(Account)?.ClassifiedEmailCount);
    }

    /// <summary>The rehearsal the run exists for: what it would file is counted and the mailbox is asked for nothing.</summary>
    [Fact]
    public async Task RunAsync_ADryRun_AsksTheMailboxForNothingAndStillCountsWhatItWouldDo()
    {
        // Arrange
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun(posture: SpamActionPosture.DryRun));

        // Act
        var report = await this.CreatePass(SpamActionSettings.Create(
            filesJunk: false,
            marksJunkRead: true,
            threshold: null)).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ActedEmailCount);
        Assert.Equal(0, this.mutations.OpenedRecordCount);
    }

    /// <summary>Mail whose content is not stored is an answer rather than a failure, and it stops no walk.</summary>
    [Fact]
    public async Task RunAsync_MailWhoseContentIsNotStored_CountsItAsUnclassifiableAndCarriesOn()
    {
        // Arrange
        var unreadable = this.StoreEmail();
        this.emailsWithoutContent.Add(unreadable);
        this.StoreEmail();
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.UnclassifiableEmailCount);
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(SpamClassificationRunEnding.Completed, report.Ending);
    }

    [Fact]
    public async Task RunAsync_MailOutsideTheRunsScopeOrOfAnotherAccount_IsNotReached()
    {
        // Arrange
        var inScope = this.StoreEmail();
        this.StoreEmail(Archive);
        this.StoreEmail(accountId: OtherAccount);
        this.runs.Arrange(this.RequestedRun());

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([inScope], this.classifications.Saved.Select(classification => classification.EmailId));
    }

    private static SpamClassificationSettings SettingsCovering(params MailFolderAlias[] aliases) =>
        SpamClassificationSettings.Create(isEnabled: true, usesScanner: false, aliases);

    /// <summary>A record an earlier pass would have left, under the terms the test names.</summary>
    private static SpamClassification ClassificationOf(StoredEmailId emailId, SpamClassificationProfile profile) =>
        SpamClassification.Create(
            emailId,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            assessment: null,
            corpusRevision: null,
            profile,
            [],
            EvaluatedAt.AddDays(-1));

    /// <summary>Builds content the pass only hands to its collaborators, so the bytes themselves say nothing.</summary>
    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA body nothing here reads.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    /// <summary>Answers with an occurrence in the inbox for whichever message the recorder asks about.</summary>
    private static ISpamActionOccurrenceReader OccurrenceReader()
    {
        var reader = Substitute.For<ISpamActionOccurrenceReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => new SpamActionOccurrence(
                call.Arg<StoredEmailId>(),
                EmailOccurrenceId.Create(
                    Account,
                    new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First),
                    ImapUidValidity.Create(9),
                    ImapUid.Create(4401)),
                Inbox,
                IsRemotelySeen: false));

        return reader;
    }

    /// <summary>Stores one occurrence the walk can reach, under an identity that orders after the ones before it.</summary>
    private StoredEmailId StoreEmail(MailFolderAlias? folderAlias = null, MailAccountId? accountId = null) =>
        this.emails.Add(new ClassifiableEmail(
            StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{++this.storedEmailCount:D12}")),
            accountId ?? Account,
            folderAlias ?? Inbox));

    private SpamClassificationRun RequestedRun(
        SpamActionPosture posture = SpamActionPosture.Acting,
        bool rescores = false)
    {
        return new SpamClassificationRun
        {
            AccountId = Account,
            RequestedAt = EvaluatedAt.AddMinutes(-1),
            Terms = SpamClassificationRunTerms.Create([Inbox], posture, rescores),
        };
    }

    private SpamClassificationPass CreatePass(
        SpamActionSettings? actions = null,
        SpamClassificationSettings? settings = null,
        int batchSize = 50,
        int maxBatchesPerPass = 4)
    {
        var classificationSettings = settings ?? SettingsCovering(Inbox);

        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.Settings.Returns(classificationSettings);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var commitPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            this.timeProvider);

        return new SpamClassificationPass(
            this.runs,
            this.emails,
            this.classifications,
            this.CreateClassifier(settingsReader, commitPolicy),
            this.CreateActionRecorder(actions ?? SpamActionSettings.None, sessionFactory, commitPolicy),
            settingsReader,
            commitPolicy,
            new SpamClassificationRunOptions { BatchSize = batchSize, MaxBatchesPerPass = maxBatchesPerPass },
            this.timeProvider);
    }

    /// <summary>Builds the real use case the pass scores through, over a header that says the mail is junk.</summary>
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

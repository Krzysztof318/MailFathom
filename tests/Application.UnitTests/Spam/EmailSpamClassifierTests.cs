// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

public sealed class EmailSpamClassifierTests
{
    private static readonly StoredEmailId Occurrence =
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000abcd"));

    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryEmailSpamClassificationStore store = new();

    private readonly IEmailContentStore contentStore = Substitute.For<IEmailContentStore>();

    [Fact]
    public async Task ClassifyAsync_ClassificationSwitchedOff_RecordsNothingAndReadsNoMail()
    {
        // Arrange
        var classifier = this.Classifier(SpamClassificationSettings.Disabled, FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.Disabled, result.Outcome);
        Assert.Null(result.Classification);
        Assert.Empty(this.store.Saved);
        await this.contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AFolderTheScopeDoesNotCover_RecordsNothingAndReadsNoMail()
    {
        // Arrange
        var classifier = this.Classifier(SettingsCovering(Junk), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.OutsideConfiguredScope, result.Outcome);
        Assert.Empty(this.store.Saved);
        await this.contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AnOccurrenceNothingIsStoredFor_ReportsItGoneRatherThanFailing()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Flag", "YES"),
            email: null,
            withEmail: false);

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.OccurrenceMissing, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    /// <summary>Mail over the size limit is stored as metadata alone, and classifying it is refused rather than fetched.</summary>
    [Fact]
    public async Task ClassifyAsync_AnOccurrenceWithNoLocalContent_ReportsItRatherThanFetchingIt()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Flag", "YES"),
            withContent: false);

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.ContentUnavailable, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    [Fact]
    public async Task ClassifyAsync_AProviderVerdictInTheHeaders_RecordsItAgainstTheOccurrence()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox),
            FactsSaying("X-Spam-Status", "Yes, score=15.2 required=5.0"));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(saved, result.Classification);
        Assert.Equal(Occurrence, saved.EmailId);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
        Assert.Equal(EvaluatedAt, saved.EvaluatedAt);
        Assert.Null(saved.CorpusRevision);
    }

    /// <summary>An arrival trigger fires per message, so a repeat has to leave an existing record alone.</summary>
    [Fact]
    public async Task ClassifyAsync_AnOccurrenceAlreadyClassified_LeavesTheRecordAlone()
    {
        // Arrange
        this.store.Hold(Recorded(SpamVerdict.NotSpam));

        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.AlreadyClassified, result.Outcome);
        Assert.Empty(this.store.Saved);
    }

    /// <summary>Replacing a verdict is what an operator asks for explicitly, and nothing else sets it off.</summary>
    [Fact]
    public async Task ClassifyAsync_AReclassificationOfAClassifiedOccurrence_ReplacesTheRecord()
    {
        // Arrange
        this.store.Hold(Recorded(SpamVerdict.NotSpam));

        var classifier = this.Classifier(SettingsCovering(Inbox), FactsSaying("X-Spam-Flag", "YES"));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.Reclassify,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
    }

    [Fact]
    public async Task ClassifyAsync_AnOccurrenceInTheJunkFolder_RecordsThePlacementAsTheReason()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Junk),
            SpamHeaderFacts.None,
            email: new ClassifiableEmail(Occurrence, Account, Junk),
            junkFolders: StubJunkMailFolderCatalog.Naming(new MailFolderIdentity(Account, Junk)));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);
        var signal = Assert.Single(saved.Signals);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
    }

    /// <summary>A scanner is the operator's added scrutiny, so it decides where the deterministic stage reached nothing.</summary>
    [Fact]
    public async Task ClassifyAsync_AScannerScoringAnUndeterminedMessage_DecidesTheVerdict()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox, usesScanner: true),
            SpamHeaderFacts.None,
            scanner: ScannerReturning(SpamScanResult.Scored(
                SpamAssessment.Create(9.5, 5.0),
                ["BAYES_99", "HTML_IMAGE_ONLY_28"],
                "4.0.2")));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Scanner, saved.DecidedBy);
        Assert.Equal("4.0.2", saved.CorpusRevision);
        Assert.Equal(
            ["BAYES_99", "HTML_IMAGE_ONLY_28"],
            saved.Signals.Where(signal => signal.Kind is SpamSignalKind.ScannerRule).Select(signal => signal.Name));
    }

    /// <summary>A post-delivery re-read has none of the network context the receiving server had.</summary>
    [Fact]
    public async Task ClassifyAsync_AScannerBelowThresholdOnMailAProviderCalledSpam_LeavesTheDeterministicVerdictStanding()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox, usesScanner: true),
            FactsSaying("X-Spam-Flag", "YES"),
            scanner: ScannerReturning(SpamScanResult.Scored(SpamAssessment.Create(0.1, 5.0), [], "4.0.2")));

        // Act
        await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamVerdict.Spam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
        Assert.Null(saved.CorpusRevision);
    }

    [Fact]
    public async Task ClassifyAsync_AScannerThatCouldNotBeReached_RecordsTheDeterministicVerdictInstead()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox, usesScanner: true),
            FactsSaying("X-Spam-Flag", "NO"),
            scanner: ScannerReturning(SpamScanResult.Unavailable()));

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamVerdict.NotSpam, saved.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, saved.DecidedBy);
        Assert.DoesNotContain(saved.Signals, signal => signal.Kind is SpamSignalKind.ScannerRule);
    }

    /// <summary>A deployment that registered no scanner still classifies, which is the whole point of the first stage.</summary>
    [Fact]
    public async Task ClassifyAsync_TheScannerSwitchedOnWithNoImplementationRegistered_StillRecordsAVerdict()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox, usesScanner: true),
            FactsSaying("X-Spam-Flag", "YES"),
            scanner: null);

        // Act
        var result = await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamClassificationOutcome.Classified, result.Outcome);
        Assert.Equal(SpamClassificationStage.Deterministic, Assert.Single(this.store.Saved).DecidedBy);
    }

    /// <summary>A scanner nobody switched on is not consulted, whatever a deployment registered.</summary>
    [Fact]
    public async Task ClassifyAsync_AScannerRegisteredButNotConfigured_IsNotConsulted()
    {
        // Arrange
        var scanner = ScannerReturning(SpamScanResult.Scored(SpamAssessment.Create(9.5, 5.0), [], "4.0.2"));
        var classifier = this.Classifier(SettingsCovering(Inbox), SpamHeaderFacts.None, scanner: scanner);

        // Act
        await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SpamVerdict.Undetermined, Assert.Single(this.store.Saved).Verdict);
        await scanner.DidNotReceive().ScanAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClassifyAsync_AConfiguredThreshold_ReplacesTheOneTheScannerAnsweredWith()
    {
        // Arrange
        var classifier = this.Classifier(
            SettingsCovering(Inbox, usesScanner: true, scannerThreshold: 12.0),
            SpamHeaderFacts.None,
            scanner: ScannerReturning(SpamScanResult.Scored(SpamAssessment.Create(9.5, 5.0), [], "4.0.2")));

        // Act
        await classifier.ClassifyAsync(
            Occurrence,
            SpamClassificationMode.FirstTimeOnly,
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.Single(this.store.Saved);

        Assert.NotNull(saved.Assessment);

        Assert.Equal(12.0, saved.Assessment.Threshold);
        Assert.Equal(9.5, saved.Assessment.Score);
        Assert.Equal(SpamVerdict.NotSpam, saved.Verdict);
    }

    [Fact]
    public async Task ClassifyAsync_AModeOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var classifier = this.Classifier(SettingsCovering(Inbox), SpamHeaderFacts.None);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => classifier.ClassifyAsync(
            Occurrence,
            (SpamClassificationMode)7,
            TestContext.Current.CancellationToken));
    }

    private static SpamClassificationSettings SettingsCovering(
        MailFolderAlias alias,
        bool usesScanner = false,
        double? scannerThreshold = null) =>
        SpamClassificationSettings.Create(isEnabled: true, usesScanner, [alias], scannerThreshold);

    private static ISpamScanner ScannerReturning(SpamScanResult result)
    {
        var scanner = Substitute.For<ISpamScanner>();
        scanner.ScanAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>()).Returns(result);

        return scanner;
    }

    private static SpamHeaderFacts FactsSaying(string fieldName, string value) =>
        SpamHeaderFacts.Create([], [new ProviderSpamHeaderValue(fieldName, value)]);

    /// <summary>Builds content the classifier only hands to its collaborators, so the bytes themselves say nothing.</summary>
    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA body nothing here reads.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    private static SpamClassification Recorded(SpamVerdict verdict) => SpamClassification.Create(
        Occurrence,
        verdict,
        SpamClassificationStage.Deterministic,
        assessment: null,
        corpusRevision: null,
        SpamClassificationProfile.Create(usesScanner: false, scannerThreshold: null),
        [],
        EvaluatedAt.AddDays(-1));

    private EmailSpamClassifier Classifier(
        SpamClassificationSettings settings,
        SpamHeaderFacts facts,
        ClassifiableEmail? email = null,
        StubJunkMailFolderCatalog? junkFolders = null,
        ISpamScanner? scanner = null,
        bool withEmail = true,
        bool withContent = true)
    {
        var emailReader = Substitute.For<IClassifiableEmailReader>();
        emailReader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(withEmail ? email ?? new ClassifiableEmail(Occurrence, Account, Inbox) : null);

        this.contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(withContent ? SomeContent() : null);

        var headerReader = Substitute.For<IEmailSpamHeaderReader>();
        headerReader.ReadAsync(Arg.Any<StoredEmailContent>(), Arg.Any<CancellationToken>()).Returns(facts);

        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.Settings.Returns(settings);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var timeProvider = new FakeTimeProvider(EvaluatedAt);

        return new EmailSpamClassifier(
            emailReader,
            this.contentStore,
            headerReader,
            junkFolders ?? StubJunkMailFolderCatalog.None,
            new DeterministicSpamClassifier(),
            settingsReader,
            this.store,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider,
            scanner);
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

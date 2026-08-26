// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Runs;

/// <summary>Covers what a pass carries, what it skips, what it leaves for the next one, and how a run stops early.</summary>
public sealed class SpamClassificationPassTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("acct-1"));

    private static readonly MailAccountIdentity OtherAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("acct-2"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create("ARCHIVE");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SpamClassificationHarness harness = new(EvaluatedAt);

    private readonly InMemorySpamClassificationRunStore runs = new();

    private readonly HashSet<StoredEmailId> emailsWithoutContent = [];

    private int storedEmailCount;

    /// <summary>Stores content for every occurrence but the ones a test says nothing is stored for.</summary>
    public SpamClassificationPassTests() => this.harness.ContentStore
        .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
        .Returns(call => this.emailsWithoutContent.Contains(call.Arg<StoredEmailId>())
            ? null
            : SpamClassificationHarness.SomeContent());

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
        Assert.Empty(this.harness.Emails.RequestedBatchSizes);
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
        Assert.Equal(2, this.harness.Classifications.Saved.Count);
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
        this.harness.Classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
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
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ARunThatRescores_ScoresMailAlreadyDecidedUnderItsProfile()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(stored, SettingsCovering(Inbox).Profile));
        this.runs.Arrange(this.RequestedRun(rescores: true));

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Equal(0, report.Walk?.SkippedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
    }

    /// <summary>A record from before the profile was part of one names terms the run cannot compare, so it is reached again.</summary>
    [Fact]
    public async Task RunAsync_ARecordNamingNoProfile_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(stored, profile: default));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
    }

    [Fact]
    public async Task RunAsync_ARecordDecidedUnderOtherTerms_IsScoredAgainRatherThanSkipped()
    {
        // Arrange
        var stored = this.StoreEmail();
        this.harness.Classifications.Hold(
            ClassificationOf(stored, SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 3)));
        this.runs.Arrange(this.RequestedRun());

        // Act
        var report = await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Walk?.ClassifiedEmailCount);
        Assert.Single(this.harness.Classifications.Saved);
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
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Empty(this.harness.Emails.RequestedBatchSizes);
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
        Assert.Empty(this.harness.Classifications.Saved);
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
            this.harness.Classifications.Saved.Select(classification => classification.EmailId));
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
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
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
        this.StoreEmail(accountId: OtherAccount.Id);
        this.runs.Arrange(this.RequestedRun());

        // Act
        await this.CreatePass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([inScope], this.harness.Classifications.Saved.Select(classification => classification.EmailId));
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

    /// <summary>Stores one occurrence the walk can reach, under an identity that orders after the ones before it.</summary>
    private StoredEmailId StoreEmail(MailFolderAlias? folderAlias = null, MailAccountId? accountId = null) =>
        this.harness.Emails.Add(new ClassifiableEmail(
            StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{++this.storedEmailCount:D12}")),
            accountId ?? Account.Id,
            folderAlias ?? Inbox));

    private SpamClassificationRun RequestedRun(
        SpamActionPosture posture = SpamActionPosture.Acting,
        bool rescores = false)
    {
        return new SpamClassificationRun
        {
            Account = Account,
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

        var sessionFactory = this.harness.CommittingSessions();
        var commitPolicy = this.harness.CommitPolicyOver(sessionFactory);

        return new SpamClassificationPass(
            this.runs,
            this.harness.Emails,
            this.harness.Classifications,
            this.harness.CreateClassifier(settingsReader, commitPolicy),
            this.harness.CreateActionRecorder(
                actions ?? SpamActionSettings.None,
                SpamClassificationHarness.OccurrenceReader(Account.Id, Inbox),
                sessionFactory,
                commitPolicy),
            settingsReader,
            commitPolicy,
            new SpamClassificationRunOptions { BatchSize = batchSize, MaxBatchesPerPass = maxBatchesPerPass },
            this.harness.Clock);
    }
}

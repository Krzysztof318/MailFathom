// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam;

/// <summary>Covers what one leased classification does, and what makes running it twice the same as running it once.</summary>
public sealed class EmailSpamClassificationHandlerTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("acct-1"));

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly EmailOccurrenceId Occurrence = EmailOccurrenceId.Create(
        Account.Id,
        new MailFolderResolutionId(Inbox, MailFolderResolutionGeneration.First),
        ImapUidValidity.Create(9),
        ImapUid.Create(4401));

    private readonly SpamClassificationHarness harness = new(EvaluatedAt);

    public EmailSpamClassificationHandlerTests() => this.harness.ContentStore
        .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
        .Returns(_ => SpamClassificationHarness.SomeContent());

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
            ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([emailId], this.harness.Classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(SpamVerdict.Spam, this.harness.Classifications.Saved.Single().Verdict);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>An attempt that committed its verdict and lost its lease leaves the next one the filing to finish.</summary>
    [Fact]
    public async Task RunAsync_AnOccurrenceAnEarlierAttemptAlreadyScored_ScoresNothingAgainAndStillActsOnTheVerdict()
    {
        // Arrange
        var emailId = this.StoreEmailAtTheOccurrence();
        this.harness.Classifications.Hold(ClassificationOf(emailId));

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>Mail expunged between the enqueue and the lease is the message leaving, not work to attempt again.</summary>
    [Fact]
    public async Task RunAsync_AnOccurrenceNothingIsStoredAt_EndsTheJobWithoutClassifyingOrActing()
    {
        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ClassificationSwitchedOff_RecordsNoVerdictAndAsksTheMailboxForNothing()
    {
        // Arrange
        this.StoreEmailAtTheOccurrence();

        // Act
        await this.CreateHandler(MarksJunkRead, SpamClassificationSettings.Disabled).RunAsync(
            ClassifyEmailSpamJobPayload.For(SyntheticMailOwner.Deployment, Occurrence),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefusedAsTheWrongWork()
    {
        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(() => this.CreateHandler().RunAsync(
            RunScheduledMailRulesJobPayload.For(Account),
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

    private StoredEmailId StoreEmailAtTheOccurrence()
    {
        var emailId = this.harness.Emails.Add(new ClassifiableEmail(
            StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
            Account.Id,
            Inbox));

        this.harness.Emails.AddOccurrence(Occurrence, emailId);

        return emailId;
    }

    private EmailSpamClassificationHandler CreateHandler(
        SpamActionSettings? actions = null,
        SpamClassificationSettings? settings = null)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Arg.Any<MailOwnerId>()).Returns(settings ?? SettingsCovering(Inbox));

        var sessionFactory = this.harness.CommittingSessions();
        var commitPolicy = this.harness.CommitPolicyOver(sessionFactory);

        return new EmailSpamClassificationHandler(
            this.harness.Emails,
            this.harness.Classifications,
            this.harness.CreateClassifier(settingsReader, commitPolicy),
            this.harness.CreateActionRecorder(
                actions ?? SpamActionSettings.None,
                SpamClassificationHarness.OccurrenceReader(Account.Id, Inbox),
                sessionFactory,
                commitPolicy));
    }
}

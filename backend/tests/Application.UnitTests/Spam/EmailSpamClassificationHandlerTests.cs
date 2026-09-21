// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.UnitTests.TestDoubles;
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
    private static readonly MailAccountId Account =
        MailAccountId.Create("acct-1");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly SpamClassificationHarness harness = new(EvaluatedAt);

    public EmailSpamClassificationHandlerTests()
    {
        this.harness.Assignments.Assigning(SyntheticUser.Deployment, Account);
        this.harness.ContentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(_ => SpamClassificationHarness.SomeContent());
    }

    [Fact]
    public void JobType_Always_IsTheClassificationOfOneStoredEmail() =>
        Assert.Equal(JobType.ClassifyStoredEmailSpam, this.CreateHandler().JobType);

    [Fact]
    public async Task RunAsync_AStoredEmailNobodyHasScored_RecordsTheVerdictAndActsOnIt()
    {
        // Arrange
        var emailId = this.StoreEmail();

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(Account, emailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([emailId], this.harness.Classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(SpamVerdict.Spam, this.harness.Classifications.Saved.Single().Verdict);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>An attempt that committed its verdict and lost its lease leaves the next one the filing to finish.</summary>
    [Fact]
    public async Task RunAsync_AStoredEmailAnEarlierAttemptAlreadyScored_ScoresNothingAgainAndStillActsOnTheVerdict()
    {
        // Arrange
        var emailId = this.StoreEmail();
        this.harness.Classifications.Hold(ClassificationOf(emailId));

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(Account, emailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(1, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>A message removed between the enqueue and the lease is the message leaving, not work to attempt again.</summary>
    [Fact]
    public async Task RunAsync_AStoredEmailNothingIsStoredUnder_EndsTheJobWithoutClassifyingOrActing()
    {
        // Arrange
        var neverStored = StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000002"));

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(Account, neverStored),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>
    /// The payload names a mailbox and a stored email as two values, so a document naming a mailbox that does not hold
    /// that email must not reach a verdict under that mailbox's posture or ask anybody's mailbox for a change.
    /// </summary>
    [Fact]
    public async Task RunAsync_AStoredEmailThePayloadsMailboxDoesNotHold_EndsTheJobWithoutClassifyingOrActing()
    {
        // Arrange
        var emailId = this.StoreEmail();
        var anotherMailbox = MailAccountId.Create("acct-2");

        // Act
        await this.CreateHandler(MarksJunkRead).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(anotherMailbox, emailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.harness.Classifications.Saved);
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    /// <summary>
    /// A message the mail server has forgotten is still mail MailFathom holds, so the job reaches it by the identity it
    /// was stored under and records a verdict — and asks the mailbox for nothing, because there is no occurrence left
    /// for a change to be written against.
    /// </summary>
    [Fact]
    public async Task RunAsync_AStoredEmailNoMailServerHoldsAnyLonger_IsStillClassifiedByItsStoredIdentity()
    {
        // Arrange
        var emailId = this.StoreEmail();
        var noOccurrence = Substitute.For<ISpamActionOccurrenceReader>();
        noOccurrence
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SpamActionOccurrence?>(null));

        // Act
        await this.CreateHandler(MarksJunkRead, occurrences: noOccurrence).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(Account, emailId),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([emailId], this.harness.Classifications.Saved.Select(classification => classification.EmailId));
        Assert.Equal(0, this.harness.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RunAsync_ClassificationSwitchedOff_RecordsNoVerdictAndAsksTheMailboxForNothing()
    {
        // Arrange
        var emailId = this.StoreEmail();

        // Act
        await this.CreateHandler(MarksJunkRead, SpamClassificationSettings.Disabled).RunAsync(
            ClassifyStoredEmailSpamJobPayload.For(Account, emailId),
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

    private StoredEmailId StoreEmail() => this.harness.Emails.Add(new ClassifiableEmail(
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
        Account,
        Inbox));

    private EmailSpamClassificationHandler CreateHandler(
        SpamActionSettings? actions = null,
        SpamClassificationSettings? settings = null,
        ISpamActionOccurrenceReader? occurrences = null)
    {
        var settingsReader = Substitute.For<ISpamClassificationSettingsReader>();
        settingsReader.SettingsFor(Arg.Any<MailAccountId>()).Returns(settings ?? SettingsCovering(Inbox));

        var sessionFactory = this.harness.CommittingSessions();
        var commitPolicy = this.harness.CommitPolicyOver(sessionFactory);

        return new EmailSpamClassificationHandler(
            this.harness.Classifications,
            this.harness.CreateClassifier(settingsReader, commitPolicy),
            this.harness.CreateActionRecorder(
                actions ?? SpamActionSettings.None,
                occurrences ?? SpamClassificationHarness.OccurrenceReader(Account, Inbox),
                sessionFactory,
                commitPolicy));
    }
}

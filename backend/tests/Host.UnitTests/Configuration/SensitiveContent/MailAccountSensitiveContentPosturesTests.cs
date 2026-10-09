// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the one place a deployment's section and an account's record become the posture its mail is read under.</summary>
/// <remarks>
/// Every answer is read from the account records a substitute reader holds, which is what a replica reads them from:
/// no replica holds every account, so nothing here is arranged through the users it serves.
/// </remarks>
public sealed class MailAccountSensitiveContentPosturesTests : IDisposable
{
    private const string AnalyzerAddress = "http://presidio-analyzer:3000";

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Archive = MailAccountId.Create("archive");

    private static readonly MailAccountId Billing = MailAccountId.Create("billing");

    private static readonly ISensitiveContentCatalog SecretsCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Secrets,
        [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);

    private static readonly ISensitiveContentCatalog PersonalDataCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Pii,
        [StubSensitiveContentCatalog.Declare("PersonName", detectedByDefault: true, "person")]);

    private readonly SensitiveContentScanConcurrency permits =
        new(SensitiveContentScanBounds.Default.MaximumConcurrentScans);

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    /// <summary>The account records the reader answers from, keyed by the account and carrying the user it is assigned to.</summary>
    private readonly Dictionary<MailAccountId, (UserId User, MailAccountScanningRequest Request)> records = [];

    private readonly IServedMailAccountReader servedAccounts = ServedMailAccountReaders.Holding();

    private int detectorResolutions;

    public MailAccountSensitiveContentPosturesTests()
    {
        this.servedAccounts
            .ReadScanningRequestsAsync(Arg.Any<UserId?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<MailAccountScanningRequest>>(
            [
                .. this.records.Values
                    .Where(record => call.Arg<UserId?>() is not { } user || record.User == user)
                    .Select(record => record.Request),
            ]));
        this.servedAccounts
            .ReadScanningRequestAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                this.records.TryGetValue(call.Arg<MailAccountId>(), out var record) ? record.Request : null));
        this.servedAccounts
            .ReadAccountsRequestingScanningAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<MailAccountScanningDeclaration>>(
            [
                .. this.records.Select(record => new MailAccountScanningDeclaration(record.Key, record.Value.Request)),
            ]));
    }

    /// <inheritdoc />
    public void Dispose() => this.permits.Dispose();

    /// <summary>
    /// The claim the whole feature rests on: two mailboxes of one deployment are scanned under what each of them
    /// asked for, and neither reads the other's answer — here both belong to one person, which is the case a posture
    /// held on the user could not tell apart.
    /// </summary>
    [Fact]
    public async Task ForAccountAsync_TwoAccountsThatAskedForDifferentThings_ScansEachUnderItsOwnAnswer()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true);
        this.Recording(SyntheticUser.Deployment, Archive, null);
        var postures = this.PosturesOver(deployment);

        // Act
        var asked = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        var askedForNothing = await postures.ForAccountAsync(Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], asked.Scanners);
        Assert.True(asked.IsActive);
        Assert.Empty(askedForNothing.Scanners);
        Assert.False(askedForNothing.IsActive);
    }

    /// <summary>
    /// A record may say <c>false</c> where the deployment says <c>true</c> — the read-back drops the rule that would
    /// refuse it, so an operator who tightened the deployment after a record was written leaves exactly this pair — and
    /// the composition is the only thing that stops it narrowing anything. Were an explicit <c>false</c> to win rather
    /// than be ignored alongside an absent switch, that mailbox's mail would be stored, chunked, embedded, and
    /// retrieved unredacted on the strength of its own record.
    /// </summary>
    [Fact]
    public async Task ForAccountAsync_ARecordDecliningAScannerTheDeploymentRequires_StillRunsItOverThatMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = false);
        var postures = this.PosturesOver(deployment);

        // Act
        var posture = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.IsActive);
    }

    /// <summary>An account tightens by adding to what the deployment requires, and what it requires stays in force.</summary>
    [Fact]
    public async Task ForAccountAsync_AnAccountTighteningWhatTheDeploymentRequires_RunsBothScannersOverItsMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true);
        this.Recording(SyntheticUser.Another, Archive, null);
        var postures = this.PosturesOver(deployment);

        // Act
        var tightener = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        var everyOtherMailbox = await postures.ForAccountAsync(Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            tightener.Scanners);
        Assert.Equal([SensitiveContentScannerKind.Secrets], everyOtherMailbox.Scanners);
    }

    /// <summary>
    /// The guard that redacts what leaves has only the person, so it reads every mailbox of theirs at once and takes
    /// the strictest — over-redacting one account's mail is the safe direction, and under-redacting another's is not.
    /// Neither mailbox's answer contains the other's, so what comes back is a union rather than the wider of the two:
    /// an account whose posture was merely the widest would leave the other's scanner off and the other's screening
    /// unapplied, on the path that decides what a search hand-out is redacted by.
    /// </summary>
    [Fact]
    public async Task AcrossAccountsOfAsync_AUserAssignedTwoAccountsAskingDifferentThings_ReadsTheUnionOfThem()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, scanning =>
        {
            scanning.Secrets.Enabled = true;
            scanning.ScreenOutgoingMailFor = ["Secrets"];
        });
        this.Recording(SyntheticUser.Deployment, Archive, scanning =>
        {
            scanning.Pii.Enabled = true;
            scanning.ScreenOutgoingMailFor = ["Pii"];
        });
        this.Recording(SyntheticUser.Another, Billing, scanning => scanning.Pii.Enabled = true);
        var postures = this.PosturesOver(deployment);

        // Act
        var posture = await postures.AcrossAccountsOfAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            posture.Scanners);
        Assert.Equal(
            SensitiveContentScannerKind.Secrets,
            posture.Screening.StoppedBy(FindingIn("CloudKey", "aws-access-token")));
        Assert.Equal(
            SensitiveContentScannerKind.Pii,
            posture.Screening.StoppedBy(FindingIn("PersonName", "person")));
    }

    /// <summary>A user whose records asked for nothing reads the deployment's own posture, as a user assigned nothing does.</summary>
    [Fact]
    public async Task AcrossAccountsOfAsync_AUserWhoseRecordsAskedForNothing_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        this.Recording(SyntheticUser.Another, Archive, null);
        var postures = this.PosturesOver(deployment);

        // Act
        var posture = await postures.AcrossAccountsOfAsync(SyntheticUser.Another, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(postures.Deployment, posture);
    }

    /// <summary>
    /// The upgrade case, and the one every existing deployment is: a mailbox whose record says nothing reads exactly
    /// the deployment's own section, which is the posture that mailbox had before the block existed.
    /// </summary>
    [Fact]
    public async Task ForAccountAsync_AnAccountThatAskedForNothing_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];
        this.Recording(SyntheticUser.Deployment, Work, null);
        var postures = this.PosturesOver(deployment);

        // Act
        var posture = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(postures.Deployment, posture);
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.ScreensAnything);
        Assert.NotNull(posture.Stamp);
    }

    /// <summary>
    /// A mailbox no record serves — never recorded, or erased since — reads the deployment's own answer rather than
    /// nothing at all, which is what every path had before an account could ask for anything.
    /// </summary>
    [Fact]
    public async Task ForAccountAsync_AnAccountNoRecordServes_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true);
        var postures = this.PosturesOver(deployment);

        // Act
        var posture = await postures.ForAccountAsync(Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(postures.Deployment, posture);
    }

    /// <summary>Two mailboxes that asked the same thing meet one posture, so a deployment holds one redaction rather than one per mailbox.</summary>
    [Fact]
    public async Task ForAccountAsync_TwoAccountsThatAskedTheSameThing_ShareOnePosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true);
        this.Recording(SyntheticUser.Another, Archive, scanning => scanning.Pii.Enabled = true);
        var postures = this.PosturesOver(deployment);

        // Act
        var first = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        var second = await postures.ForAccountAsync(Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, this.detectorResolutions);
    }

    /// <summary>One account's mail is read message by message, so its answer is read once and reused rather than read per message.</summary>
    [Fact]
    public async Task ForAccountAsync_AskedAgainWithinTheFreshness_ReadsTheRecordOnce()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true);
        var postures = this.PosturesOver(new SensitiveContentOptions());

        // Act
        var first = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        this.timeProvider.Advance(MailAccountSensitiveContentPostures.Freshness - TimeSpan.FromSeconds(1));
        var second = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(first, second);
        await this.servedAccounts.Received(1).ReadScanningRequestAsync(Work, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A record committed after an answer was read — here, by another replica — is what the mailbox is read under once
    /// the answer is no longer fresh. Nothing else would carry it here: the answer is held rather than read per message.
    /// </summary>
    [Fact]
    public async Task ForAccountAsync_ARecordCommittedAfterTheFreshnessElapsed_IsReadUnderWhatItAsksFor()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, null);
        var postures = this.PosturesOver(new SensitiveContentOptions());
        var before = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true);

        // Act
        this.timeProvider.Advance(MailAccountSensitiveContentPostures.Freshness);
        var after = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(before.IsActive);
        Assert.Equal([SensitiveContentScannerKind.Secrets], after.Scanners);
    }

    /// <summary>
    /// A rebuild decides which rows are stale from the reading of every account and re-derives each through the answer
    /// about one, so that reading replaces an answer held from before the account's last commit rather than leaving the
    /// walk to stamp rows under the posture it has just moved past.
    /// </summary>
    [Fact]
    public async Task ReadAccountsBeyondDeploymentAsync_AnAnswerHeldFromBeforeACommit_IsReplacedByWhatTheReadingFound()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, null);
        var postures = this.PosturesOver(new SensitiveContentOptions());
        await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true);

        // Act
        var beyond = await postures.ReadAccountsBeyondDeploymentAsync(TestContext.Current.CancellationToken);
        var held = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(Assert.Single(beyond).Posture, held);
        await this.servedAccounts.Received(1).ReadScanningRequestAsync(Work, Arg.Any<CancellationToken>());
    }

    /// <summary>One mailbox's write leaves every other mailbox's posture exactly where it was.</summary>
    [Fact]
    public async Task ForAccountAsync_OneAccountCommittingARecord_LeavesAnotherAccountsPostureAsItWas()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, null);
        this.Recording(SyntheticUser.Another, Archive, null);
        var postures = this.PosturesOver(new SensitiveContentOptions());
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true);

        // Act
        var committed = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);
        var untouched = await postures.ForAccountAsync(Archive, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(committed.IsActive);
        Assert.False(untouched.IsActive);
    }

    /// <summary>
    /// The readiness probe of the analyzer asks about a scanner rather than about a mailbox, because a dependency
    /// nobody's mail reaches is not one this deployment is unhealthy without.
    /// </summary>
    [Fact]
    public async Task RunsForAnyAccountAsync_OneAccountThatAskedForAScannerTheDeploymentLeftOff_ReportsThatItRuns()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, null);
        this.Recording(SyntheticUser.Another, Archive, scanning => scanning.Pii.Enabled = true);
        var postures = this.PosturesOver(deployment);

        // Act
        var runsPersonalData = await postures.RunsForAnyAccountAsync(SensitiveContentScannerKind.Pii, TestContext.Current.CancellationToken);
        var runsSecrets = await postures.RunsForAnyAccountAsync(SensitiveContentScannerKind.Secrets, TestContext.Current.CancellationToken);
        var isActive = await postures.IsActiveForAnyAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(runsPersonalData);
        Assert.False(runsSecrets);
        Assert.True(isActive);
        await this.servedAccounts.Received(3).ReadScanningRequestsAsync(null, Arg.Any<CancellationToken>());
    }

    /// <summary>The control for the answer above: records asking for nothing leave a deployment that switched nothing on inactive.</summary>
    [Fact]
    public async Task IsActiveForAnyAccountAsync_RecordsAskingForNothing_ReportsInactive()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, null);
        var postures = this.PosturesOver(new SensitiveContentOptions());

        // Act
        var isActive = await postures.IsActiveForAnyAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(isActive);
    }

    /// <summary>An opt-in nobody took costs nothing: no plan is composed, no detector is constructed, and no permit is held.</summary>
    [Fact]
    public async Task ForAccountAsync_ADeploymentNobodyIsScannedFor_ConstructsNoDetectorAtAll()
    {
        // Arrange
        this.Recording(SyntheticUser.Deployment, Work, null);
        var postures = this.PosturesOver(new SensitiveContentOptions());

        // Act
        var posture = await postures.ForAccountAsync(Work, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(SensitiveContentPosture.ScanningNothing, posture);
        Assert.False(await postures.IsActiveForAnyAccountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, this.detectorResolutions);
    }

    /// <summary>
    /// The walk that re-derives stale rows reads every account whose mail is scanned under something other than the
    /// deployment's own posture, each beside its own, ordered by account — and nothing about the accounts that read the
    /// deployment's, which the walk covers through that one posture instead.
    /// </summary>
    [Fact]
    public async Task ReadAccountsBeyondDeploymentAsync_SeveralAccounts_ReportsOnlyThoseScannedBeyondTheDeploymentInOrder()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        this.Recording(SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true);
        this.Recording(SyntheticUser.Another, Archive, null);
        this.Recording(SyntheticUser.Another, Billing, scanning => scanning.Secrets.Enabled = true);
        var postures = this.PosturesOver(deployment);

        // Act
        var beyond = await postures.ReadAccountsBeyondDeploymentAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(Billing, SensitiveContentScannerKind.Secrets), (Work, SensitiveContentScannerKind.Pii)],
            beyond.Select(account => (account.Account, Assert.Single(account.Posture.Scanners))));
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            null!,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            this.servedAccounts));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            null!,
            this.Detectors,
            TimeProvider.System,
            this.permits,
            this.servedAccounts));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            null!,
            TimeProvider.System,
            this.permits,
            this.servedAccounts));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            null!,
            this.permits,
            this.servedAccounts));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            null!,
            this.servedAccounts));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            null!));
    }

    /// <summary>Builds a finding a screening policy is asked about, which reads its category and nothing else.</summary>
    private static SensitiveContentFinding FindingIn(string category, string rule) =>
        SensitiveContentFinding.Create(
            SensitiveContentRule.Create(SensitiveContentCategory.Create(category), rule),
            SensitiveContentSpan.Create(0, 4),
            confidence: 1,
            SensitiveContentDetector.Create("stubbed", "1"),
            DateTimeOffset.UnixEpoch);

    /// <summary>Builds what one account's record answers about scanning, as the account records are read into.</summary>
    /// <remarks>
    /// The block is configured rather than assigned, because an account's scanning settings are a block it owns: a
    /// record states properties inside it and never replaces it.
    /// </remarks>
    private static MailAccountScanningRequest RequestOf(MailAccountId account, Action<MailAccountSensitiveContentOptions>? asking)
    {
        var declared = new MailSynchronizationAccountOptions { AccountId = account.Value };

        asking?.Invoke(declared.SensitiveContent);

        return new MailAccountScanningRequest(
            [.. Enum.GetValues<SensitiveContentScannerKind>().Where(scanner => declared.SensitiveContent.For(scanner).Enabled is true)],
            declared.SensitiveContent.ScreenedScanners);
    }

    /// <summary>Records one account assigned to a user, carrying whatever its record asked to be scanned for.</summary>
    private void Recording(UserId user, MailAccountId account, Action<MailAccountSensitiveContentOptions>? asking) =>
        this.records[account] = (user, RequestOf(account, asking));

    private MailAccountSensitiveContentPostures PosturesOver(SensitiveContentOptions deployment) => new(
        deployment,
        [SecretsCatalog, PersonalDataCatalog],
        this.Detectors,
        this.timeProvider,
        this.permits,
        this.servedAccounts);

    /// <summary>
    /// Stands in for the detectors the composition root registered, and counts how often they were asked for. Resolving
    /// them is what constructs a regular-expression corpus and an analyzer client, so the count is the evidence that a
    /// posture nobody reads costs nothing and that two mailboxes asking the same thing pay for one.
    /// </summary>
    private IEnumerable<ISensitiveContentScanner> Detectors()
    {
        this.detectorResolutions++;

        return
        [
            new MarkerSensitiveContentScanner("AKIAEXAMPLEKEY", SensitiveContentScannerKind.Secrets, TimeProvider.System),
            new MarkerSensitiveContentScanner("Ada Lovelace", SensitiveContentScannerKind.Pii, TimeProvider.System),
        ];
    }
}

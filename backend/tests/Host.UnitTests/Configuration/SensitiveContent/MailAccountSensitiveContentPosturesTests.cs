// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the one place a deployment's section and an account's record become the posture its mail is read under.</summary>
public sealed class MailAccountSensitiveContentPosturesTests : IDisposable
{
    private const string AnalyzerAddress = "http://presidio-analyzer:3000";

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Archive = MailAccountId.Create("archive");

    private static readonly ISensitiveContentCatalog SecretsCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Secrets,
        [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);

    private static readonly ISensitiveContentCatalog PersonalDataCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Pii,
        [StubSensitiveContentCatalog.Declare("PersonName", detectedByDefault: true, "person")]);

    private readonly SensitiveContentScanConcurrency permits =
        new(SensitiveContentScanBounds.Default.MaximumConcurrentScans);

    private int detectorResolutions;

    /// <inheritdoc />
    public void Dispose() => this.permits.Dispose();

    /// <summary>
    /// The claim the whole feature rests on: two mailboxes of one deployment are scanned under what each of them
    /// asked for, and neither reads the other's answer — here both belong to one person, which is the case a posture
    /// held on the user could not tell apart.
    /// </summary>
    [Fact]
    public void ForAccount_TwoAccountsThatAskedForDifferentThings_ScansEachUnderItsOwnAnswer()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = true),
            (SyntheticUser.Deployment, Archive, null));

        // Act
        var asked = postures.ForAccount(Work);
        var askedForNothing = postures.ForAccount(Archive);

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
    public void ForAccount_ARecordDecliningAScannerTheDeploymentRequires_StillRunsItOverThatMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, scanning => scanning.Secrets.Enabled = false));

        // Act
        var posture = postures.ForAccount(Work);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.IsActive);
    }

    /// <summary>An account tightens by adding to what the deployment requires, and what it requires stays in force.</summary>
    [Fact]
    public void ForAccount_AnAccountTighteningWhatTheDeploymentRequires_RunsBothScannersOverItsMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true),
            (SyntheticUser.Another, Archive, null));

        // Act
        var tightener = postures.ForAccount(Work);
        var everyOtherMailbox = postures.ForAccount(Archive);

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
    public void AcrossAccountsOf_AUserAssignedTwoAccountsAskingDifferentThings_ReadsTheUnionOfThem()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;

        Action<MailAccountSensitiveContentOptions> screeningSecrets = scanning =>
        {
            scanning.Secrets.Enabled = true;
            scanning.ScreenOutgoingMailFor = ["Secrets"];
        };

        Action<MailAccountSensitiveContentOptions> screeningPersonalData = scanning =>
        {
            scanning.Pii.Enabled = true;
            scanning.ScreenOutgoingMailFor = ["Pii"];
        };

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, screeningSecrets),
            (SyntheticUser.Deployment, Archive, screeningPersonalData));

        // Act
        var posture = postures.AcrossAccountsOf(SyntheticUser.Deployment);

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

    /// <summary>
    /// The upgrade case, and the one every existing deployment is: a roster of one mailbox whose record says nothing
    /// reads exactly the deployment's own section, which is the posture that mailbox had before the block existed.
    /// </summary>
    [Fact]
    public void ForAccount_OneAccountThatAskedForNothing_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];

        var postures = this.PosturesOver(deployment, (SyntheticUser.Deployment, Work, null));

        // Act
        var posture = postures.ForAccount(Work);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.ScreensAnything);
        Assert.NotNull(posture.Stamp);
    }

    /// <summary>A mailbox the roster does not name reads the deployment's own answer rather than nothing at all.</summary>
    [Fact]
    public void ForAccount_AnAccountThisRosterDoesNotName_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, (SyntheticUser.Deployment, Work, null));

        // Act
        var posture = postures.ForAccount(Archive);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
    }

    /// <summary>
    /// Before the startup gate has established the roster there is no record to read, and the answer is the deployment's
    /// own — which is what every path had before any of this existed, rather than a refusal a worker would meet.
    /// </summary>
    [Fact]
    public void ForAccount_BeforeTheStartupGateHasRun_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, new ServedUsers());

        // Act
        var posture = postures.ForAccount(Work);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.Empty(postures.Current);
    }

    /// <summary>Two mailboxes that asked the same thing meet one posture, so a deployment holds one redaction rather than one per mailbox.</summary>
    [Fact]
    public void ForAccount_TwoAccountsThatAskedTheSameThing_ShareOnePosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, null),
            (SyntheticUser.Another, Archive, null));

        // Act
        var first = postures.ForAccount(Work);
        var second = postures.ForAccount(Archive);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, this.detectorResolutions);
    }

    /// <summary>
    /// The readiness probe of the analyzer asks about a scanner rather than about a mailbox, because a dependency
    /// nobody's mail reaches is not one this deployment is unhealthy without.
    /// </summary>
    [Fact]
    public void RunsForAnyAccount_OneAccountThatAskedForAScannerTheDeploymentLeftOff_ReportsThatItRuns()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, null),
            (SyntheticUser.Another, Archive, scanning => scanning.Pii.Enabled = true));

        // Act, Assert
        Assert.True(postures.RunsForAnyAccount(SensitiveContentScannerKind.Pii));
        Assert.False(postures.RunsForAnyAccount(SensitiveContentScannerKind.Secrets));
        Assert.True(postures.IsActiveForAnyAccount);
    }

    /// <summary>An opt-in nobody took costs nothing: no plan is composed, no detector is constructed, and no permit is held.</summary>
    [Fact]
    public void ForAccount_ADeploymentNobodyIsScannedFor_ConstructsNoDetectorAtAll()
    {
        // Arrange
        var postures = this.PosturesOver(
            new SensitiveContentOptions(),
            (SyntheticUser.Deployment, Work, null));

        // Act
        var posture = postures.ForAccount(Work);

        // Assert
        Assert.Same(SensitiveContentPosture.ScanningNothing, posture);
        Assert.False(postures.IsActiveForAnyAccount);
        Assert.Equal(0, this.detectorResolutions);
    }

    /// <summary>
    /// A record committed after the process started replaces the roster, and the postures follow it. Nothing else would:
    /// the composition is held rather than computed per read, so a mailbox whose record switched a scanner on would go
    /// on being read under the answer it had before that write.
    /// </summary>
    [Fact]
    public void ForAccount_ARecordCommittedAfterTheRosterWasEstablished_IsReadUnderWhatItAsksFor()
    {
        // Arrange
        var servedUsers = new ServedUsers();

        servedUsers.Resolved([Serving(SyntheticUser.Deployment, (Work, null))]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedUsers);

        Assert.False(postures.ForAccount(Work).IsActive);

        var asked = new UserAccountOptions
        {
            MailAccounts = [Account(Work, scanning => scanning.Secrets.Enabled = true)],
        };

        // Act
        servedUsers.UserDocumentPublished(SyntheticUser.Deployment, "user", asked, 1);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets],
            postures.ForAccount(Work).Scanners);
    }

    /// <summary>One mailbox's write leaves every other mailbox's posture exactly where it was.</summary>
    [Fact]
    public void ForAccount_OneAccountCommittingARecord_LeavesAnotherAccountsPostureAsItWas()
    {
        // Arrange
        var servedUsers = new ServedUsers();

        servedUsers.Resolved(
        [
            Serving(SyntheticUser.Deployment, (Work, null)),
            Serving(SyntheticUser.Another, (Archive, null)),
        ]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedUsers);
        var asked = new UserAccountOptions
        {
            MailAccounts = [Account(Work, scanning => scanning.Secrets.Enabled = true)],
        };

        // Act
        servedUsers.UserDocumentPublished(SyntheticUser.Deployment, "user", asked, 1);

        // Assert
        Assert.True(postures.ForAccount(Work).IsActive);
        Assert.False(postures.ForAccount(Archive).IsActive);
    }

    /// <summary>The walk that re-derives stale rows reads every mailbox from here, so each arrives with its own posture.</summary>
    [Fact]
    public void Current_ARosterOfSeveralAccounts_ReportsEachOfThemBesideWhatItsMailIsScannedFor()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticUser.Deployment, Work, scanning => scanning.Pii.Enabled = true),
            (SyntheticUser.Another, Archive, null));

        // Act
        var current = postures.Current;

        // Assert
        Assert.Equal([Archive, Work], current.Select(account => account.Account));
        Assert.Empty(current[0].Posture.Scanners);
        Assert.Equal([SensitiveContentScannerKind.Pii], current[1].Posture.Scanners);
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        var servedUsers = new ServedUsers();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            null!,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            null!,
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            null!,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            null!,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new MailAccountSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            null!,
            servedUsers));
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

    /// <summary>Builds the roster entry a user assigned these mailboxes arrives as.</summary>
    private static ServedUser Serving(
        UserId user,
        params (MailAccountId Account, Action<MailAccountSensitiveContentOptions>? Asking)[] accounts) =>
        new(
            user,
            user.Value.ToString(),
            [.. accounts.Select(entry => Account(entry.Account, entry.Asking))]);

    /// <summary>Builds one declared mailbox carrying whatever its record asked to be scanned for.</summary>
    /// <remarks>
    /// The block is configured rather than assigned, because an account's scanning settings are a block it owns: a
    /// record states properties inside it and never replaces it.
    /// </remarks>
    private static MailSynchronizationAccountOptions Account(
        MailAccountId account,
        Action<MailAccountSensitiveContentOptions>? asking)
    {
        var declared = new MailSynchronizationAccountOptions { AccountId = account.Value };

        asking?.Invoke(declared.SensitiveContent);

        return declared;
    }

    /// <summary>Composes the postures of a deployment whose roster is settled and names exactly these mailboxes.</summary>
    private MailAccountSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        params (UserId User, MailAccountId Account, Action<MailAccountSensitiveContentOptions>? Asking)[] accounts)
    {
        var servedUsers = new ServedUsers();

        servedUsers.Resolved(
        [
            .. accounts
                .GroupBy(entry => entry.User)
                .Select(assigned => Serving(
                    assigned.Key,
                    [.. assigned.Select(entry => (entry.Account, entry.Asking))])),
        ]);

        return this.PosturesOver(deployment, servedUsers);
    }

    /// <summary>Composes the postures over a roster the test drives itself, which is how a later write is exercised.</summary>
    private MailAccountSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        ServedUsers servedUsers) => new(
        deployment,
        [SecretsCatalog, PersonalDataCatalog],
        this.Detectors,
        TimeProvider.System,
        this.permits,
        servedUsers);

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

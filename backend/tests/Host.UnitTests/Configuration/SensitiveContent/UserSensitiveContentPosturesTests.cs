// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the one place a deployment's section and a user's record become the posture their mail is read under.</summary>
public sealed class UserSensitiveContentPosturesTests : IDisposable
{
    private const string AnalyzerAddress = "http://presidio-analyzer:3000";

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
    /// The claim the whole feature rests on: two people served by one deployment are scanned under what each of them
    /// asked for, and neither reads the other's answer.
    /// </summary>
    [Fact]
    public void ForUser_TwoUsersWhoAskedForDifferentThings_ScansEachUnderTheirOwnAnswer()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var scanned = new UserSensitiveContentOptions();
        scanned.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailUser.Deployment, scanned),
            (SyntheticMailUser.Another, new UserSensitiveContentOptions()));

        // Act
        var asked = postures.ForUser(SyntheticMailUser.Deployment);
        var askedForNothing = postures.ForUser(SyntheticMailUser.Another);

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
    /// than be ignored alongside an absent switch, that user's mail would be stored, chunked, embedded, and retrieved
    /// unredacted on the strength of their own record.
    /// </summary>
    [Fact]
    public void ForUser_ARecordDecliningAScannerTheDeploymentRequires_StillRunsItOverTheirMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        var declined = new UserSensitiveContentOptions();
        declined.Secrets.Enabled = false;

        var postures = this.PosturesOver(deployment, (SyntheticMailUser.Deployment, declined));

        // Act
        var posture = postures.ForUser(SyntheticMailUser.Deployment);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.IsActive);
    }

    /// <summary>A user tightens by adding to what the deployment requires, and what it requires stays in force.</summary>
    [Fact]
    public void ForUser_AUserTighteningWhatTheDeploymentRequires_RunsBothScannersOverTheirMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var tightened = new UserSensitiveContentOptions();
        tightened.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailUser.Deployment, tightened),
            (SyntheticMailUser.Another, new UserSensitiveContentOptions()));

        // Act
        var tightener = postures.ForUser(SyntheticMailUser.Deployment);
        var everybodyElse = postures.ForUser(SyntheticMailUser.Another);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            tightener.Scanners);
        Assert.Equal([SensitiveContentScannerKind.Secrets], everybodyElse.Scanners);
    }

    /// <summary>
    /// The upgrade case, and the one every existing deployment is: a roster of one user whose record says nothing
    /// reads exactly the deployment's own section, which is the posture that user had before the block existed.
    /// </summary>
    [Fact]
    public void ForUser_OneUserWhoAskedForNothing_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];

        var postures = this.PosturesOver(deployment, (SyntheticMailUser.Deployment, new UserSensitiveContentOptions()));

        // Act
        var posture = postures.ForUser(SyntheticMailUser.Deployment);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.ScreensAnything);
        Assert.NotNull(posture.Stamp);
    }

    /// <summary>A user the roster does not name reads the deployment's own answer rather than nothing at all.</summary>
    [Fact]
    public void ForUser_AUserThisRosterDoesNotName_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, (SyntheticMailUser.Deployment, new UserSensitiveContentOptions()));

        // Act
        var posture = postures.ForUser(SyntheticMailUser.Another);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
    }

    /// <summary>
    /// Before the startup gate has established the roster there is no record to read, and the answer is the deployment's
    /// own — which is what every path had before any of this existed, rather than a refusal a worker would meet.
    /// </summary>
    [Fact]
    public void ForUser_BeforeTheStartupGateHasRun_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, new ServedMailUsers());

        // Act
        var posture = postures.ForUser(SyntheticMailUser.Deployment);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.Empty(postures.Current);
    }

    /// <summary>Two users who asked the same thing meet one posture, so a deployment holds one redaction rather than one per person.</summary>
    [Fact]
    public void ForUser_TwoUsersWhoAskedTheSameThing_ShareOnePosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailUser.Deployment, new UserSensitiveContentOptions()),
            (SyntheticMailUser.Another, new UserSensitiveContentOptions()));

        // Act
        var first = postures.ForUser(SyntheticMailUser.Deployment);
        var second = postures.ForUser(SyntheticMailUser.Another);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, this.detectorResolutions);
    }

    /// <summary>
    /// The readiness probe of the analyzer asks about a scanner rather than about a user, because a dependency
    /// nobody's mail reaches is not one this deployment is unhealthy without.
    /// </summary>
    [Fact]
    public void RunsForAnyUser_OneUserWhoAskedForAScannerTheDeploymentLeftOff_ReportsThatItRuns()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var asked = new UserSensitiveContentOptions();
        asked.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailUser.Deployment, new UserSensitiveContentOptions()),
            (SyntheticMailUser.Another, asked));

        // Act, Assert
        Assert.True(postures.RunsForAnyUser(SensitiveContentScannerKind.Pii));
        Assert.False(postures.RunsForAnyUser(SensitiveContentScannerKind.Secrets));
        Assert.True(postures.IsActiveForAnyUser);
    }

    /// <summary>An opt-in nobody took costs nothing: no plan is composed, no detector is constructed, and no permit is held.</summary>
    [Fact]
    public void ForUser_ADeploymentNobodyIsScannedFor_ConstructsNoDetectorAtAll()
    {
        // Arrange
        var postures = this.PosturesOver(
            new SensitiveContentOptions(),
            (SyntheticMailUser.Deployment, new UserSensitiveContentOptions()));

        // Act
        var posture = postures.ForUser(SyntheticMailUser.Deployment);

        // Assert
        Assert.Same(SensitiveContentPosture.ScanningNothing, posture);
        Assert.False(postures.IsActiveForAnyUser);
        Assert.Equal(0, this.detectorResolutions);
    }

    /// <summary>
    /// A record committed after the process started replaces the roster, and the postures follow it. Nothing else would:
    /// the composition is held rather than computed per read, so a user who switched a scanner on would go on being
    /// read under the answer they had before their write.
    /// </summary>
    [Fact]
    public void ForUser_ARecordCommittedAfterTheRosterWasEstablished_IsReadUnderWhatItAsksFor()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        servedUsers.Resolved([Serving(SyntheticMailUser.Deployment, new UserSensitiveContentOptions())]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedUsers);

        Assert.False(postures.ForUser(SyntheticMailUser.Deployment).IsActive);

        var asked = new UserAccountOptions();
        asked.SensitiveContent.Secrets.Enabled = true;

        // Act
        servedUsers.UserDocumentPublished(SyntheticMailUser.Deployment, "user", asked, 1);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets],
            postures.ForUser(SyntheticMailUser.Deployment).Scanners);
    }

    /// <summary>One user's write leaves everybody else's posture exactly where it was.</summary>
    [Fact]
    public void ForUser_OneUserCommittingARecord_LeavesAnotherUsersPostureAsItWas()
    {
        // Arrange
        var servedUsers = new ServedMailUsers();

        servedUsers.Resolved(
        [
            Serving(SyntheticMailUser.Deployment, new UserSensitiveContentOptions()),
            Serving(SyntheticMailUser.Another, new UserSensitiveContentOptions()),
        ]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedUsers);
        var asked = new UserAccountOptions();
        asked.SensitiveContent.Secrets.Enabled = true;

        // Act
        servedUsers.UserDocumentPublished(SyntheticMailUser.Deployment, "user", asked, 1);

        // Assert
        Assert.True(postures.ForUser(SyntheticMailUser.Deployment).IsActive);
        Assert.False(postures.ForUser(SyntheticMailUser.Another).IsActive);
    }

    /// <summary>The walk that re-derives stale rows reads every user from here, so each of them arrives with their own posture.</summary>
    [Fact]
    public void Current_ARosterOfSeveralUsers_ReportsEachOfThemBesideWhatTheirMailIsScannedFor()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var asked = new UserSensitiveContentOptions();
        asked.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailUser.Deployment, asked),
            (SyntheticMailUser.Another, new UserSensitiveContentOptions()));

        // Act
        var current = postures.Current;

        // Assert
        Assert.Equal(
            [SyntheticMailUser.Deployment, SyntheticMailUser.Another],
            current.Select(user => user.User));
        Assert.Equal([SensitiveContentScannerKind.Pii], current[0].Posture.Scanners);
        Assert.Empty(current[1].Posture.Scanners);
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        var servedUsers = new ServedMailUsers();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            null!,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            deployment,
            null!,
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            deployment,
            [],
            null!,
            TimeProvider.System,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            null!,
            this.permits,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            null!,
            servedUsers));
        Assert.Throws<ArgumentNullException>(() => new UserSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            null!));
    }

    /// <summary>Builds the roster entry a user declared in the deployment's own file arrives as.</summary>
    private static ServedMailUser Serving(MailUserId user, UserSensitiveContentOptions sensitiveContent) =>
        new(
            user,
            user.Value.ToString(),
            MailUserAccountSource.DeploymentSection,
            [],
            SensitiveContent: sensitiveContent);

    /// <summary>Composes the postures of a deployment whose roster is settled and names exactly these users.</summary>
    private UserSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        params (MailUserId User, UserSensitiveContentOptions SensitiveContent)[] users)
    {
        var servedUsers = new ServedMailUsers();

        servedUsers.Resolved([.. users.Select(entry => Serving(entry.User, entry.SensitiveContent))]);

        return this.PosturesOver(deployment, servedUsers);
    }

    /// <summary>Composes the postures over a roster the test drives itself, which is how a later write is exercised.</summary>
    private UserSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        ServedMailUsers servedUsers) => new(
        deployment,
        [SecretsCatalog, PersonalDataCatalog],
        this.Detectors,
        TimeProvider.System,
        this.permits,
        servedUsers);

    /// <summary>
    /// Stands in for the detectors the composition root registered, and counts how often they were asked for. Resolving
    /// them is what constructs a regular-expression corpus and an analyzer client, so the count is the evidence that a
    /// posture nobody reads costs nothing and that two users asking the same thing pay for one.
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

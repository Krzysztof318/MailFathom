// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the one place a deployment's section and an owner's record become the posture their mail is read under.</summary>
public sealed class OwnerSensitiveContentPosturesTests : IDisposable
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
    public void ForOwner_TwoOwnersWhoAskedForDifferentThings_ScansEachUnderTheirOwnAnswer()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var scanned = new OwnerSensitiveContentOptions();
        scanned.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailOwner.Deployment, scanned),
            (SyntheticMailOwner.Another, new OwnerSensitiveContentOptions()));

        // Act
        var asked = postures.ForOwner(SyntheticMailOwner.Deployment);
        var askedForNothing = postures.ForOwner(SyntheticMailOwner.Another);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], asked.Scanners);
        Assert.True(asked.IsActive);
        Assert.Empty(askedForNothing.Scanners);
        Assert.False(askedForNothing.IsActive);
    }

    /// <summary>An owner tightens by adding to what the deployment requires, and what it requires stays in force.</summary>
    [Fact]
    public void ForOwner_AnOwnerTighteningWhatTheDeploymentRequires_RunsBothScannersOverTheirMail()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var tightened = new OwnerSensitiveContentOptions();
        tightened.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailOwner.Deployment, tightened),
            (SyntheticMailOwner.Another, new OwnerSensitiveContentOptions()));

        // Act
        var tightener = postures.ForOwner(SyntheticMailOwner.Deployment);
        var everybodyElse = postures.ForOwner(SyntheticMailOwner.Another);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            tightener.Scanners);
        Assert.Equal([SensitiveContentScannerKind.Secrets], everybodyElse.Scanners);
    }

    /// <summary>
    /// The upgrade case, and the one every existing deployment is: a roster of one owner whose record says nothing
    /// reads exactly the deployment's own section, which is the posture that owner had before the block existed.
    /// </summary>
    [Fact]
    public void ForOwner_OneOwnerWhoAskedForNothing_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;
        deployment.ScreenOutgoingMailFor = ["Secrets"];

        var postures = this.PosturesOver(deployment, (SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()));

        // Act
        var posture = postures.ForOwner(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.True(posture.ScreensAnything);
        Assert.NotNull(posture.Stamp);
    }

    /// <summary>An owner the roster does not name reads the deployment's own answer rather than nothing at all.</summary>
    [Fact]
    public void ForOwner_AnOwnerThisRosterDoesNotName_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, (SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()));

        // Act
        var posture = postures.ForOwner(SyntheticMailOwner.Another);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
    }

    /// <summary>
    /// Before the startup gate has established the roster there is no record to read, and the answer is the deployment's
    /// own — which is what every path had before any of this existed, rather than a refusal a worker would meet.
    /// </summary>
    [Fact]
    public void ForOwner_BeforeTheStartupGateHasRun_ReadsTheDeploymentsOwnPosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(deployment, new ServedMailOwners());

        // Act
        var posture = postures.ForOwner(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], posture.Scanners);
        Assert.Empty(postures.Current);
    }

    /// <summary>Two owners who asked the same thing meet one posture, so a deployment holds one redaction rather than one per person.</summary>
    [Fact]
    public void ForOwner_TwoOwnersWhoAskedTheSameThing_ShareOnePosture()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.Secrets.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()),
            (SyntheticMailOwner.Another, new OwnerSensitiveContentOptions()));

        // Act
        var first = postures.ForOwner(SyntheticMailOwner.Deployment);
        var second = postures.ForOwner(SyntheticMailOwner.Another);

        // Assert
        Assert.Same(first, second);
        Assert.Equal(1, this.detectorResolutions);
    }

    /// <summary>
    /// The readiness probe of the analyzer asks about a scanner rather than about an owner, because a dependency
    /// nobody's mail reaches is not one this deployment is unhealthy without.
    /// </summary>
    [Fact]
    public void RunsForAnyOwner_OneOwnerWhoAskedForAScannerTheDeploymentLeftOff_ReportsThatItRuns()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var asked = new OwnerSensitiveContentOptions();
        asked.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()),
            (SyntheticMailOwner.Another, asked));

        // Act, Assert
        Assert.True(postures.RunsForAnyOwner(SensitiveContentScannerKind.Pii));
        Assert.False(postures.RunsForAnyOwner(SensitiveContentScannerKind.Secrets));
        Assert.True(postures.IsActiveForAnyOwner);
    }

    /// <summary>An opt-in nobody took costs nothing: no plan is composed, no detector is constructed, and no permit is held.</summary>
    [Fact]
    public void ForOwner_ADeploymentNobodyIsScannedFor_ConstructsNoDetectorAtAll()
    {
        // Arrange
        var postures = this.PosturesOver(
            new SensitiveContentOptions(),
            (SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()));

        // Act
        var posture = postures.ForOwner(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Same(SensitiveContentPosture.ScanningNothing, posture);
        Assert.False(postures.IsActiveForAnyOwner);
        Assert.Equal(0, this.detectorResolutions);
    }

    /// <summary>
    /// A record committed after the process started replaces the roster, and the postures follow it. Nothing else would:
    /// the composition is held rather than computed per read, so an owner who switched a scanner on would go on being
    /// read under the answer they had before their write.
    /// </summary>
    [Fact]
    public void ForOwner_ARecordCommittedAfterTheRosterWasEstablished_IsReadUnderWhatItAsksFor()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        servedOwners.Resolved([Serving(SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions())]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedOwners);

        Assert.False(postures.ForOwner(SyntheticMailOwner.Deployment).IsActive);

        var asked = new OwnerSensitiveContentOptions();
        asked.Secrets.Enabled = true;

        // Act
        servedOwners.OwnerDocumentPublished(SyntheticMailOwner.Deployment, "owner", [], asked, 1);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets],
            postures.ForOwner(SyntheticMailOwner.Deployment).Scanners);
    }

    /// <summary>One owner's write leaves everybody else's posture exactly where it was.</summary>
    [Fact]
    public void ForOwner_OneOwnerCommittingARecord_LeavesAnotherOwnersPostureAsItWas()
    {
        // Arrange
        var servedOwners = new ServedMailOwners();

        servedOwners.Resolved(
        [
            Serving(SyntheticMailOwner.Deployment, new OwnerSensitiveContentOptions()),
            Serving(SyntheticMailOwner.Another, new OwnerSensitiveContentOptions()),
        ]);

        var postures = this.PosturesOver(new SensitiveContentOptions(), servedOwners);
        var asked = new OwnerSensitiveContentOptions();
        asked.Secrets.Enabled = true;

        // Act
        servedOwners.OwnerDocumentPublished(SyntheticMailOwner.Deployment, "owner", [], asked, 1);

        // Assert
        Assert.True(postures.ForOwner(SyntheticMailOwner.Deployment).IsActive);
        Assert.False(postures.ForOwner(SyntheticMailOwner.Another).IsActive);
    }

    /// <summary>The walk that re-derives stale rows reads every owner from here, so each of them arrives with their own posture.</summary>
    [Fact]
    public void Current_ARosterOfSeveralOwners_ReportsEachOfThemBesideWhatTheirMailIsScannedFor()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        deployment.PersonalDataAnalyzer.Endpoint = AnalyzerAddress;
        var asked = new OwnerSensitiveContentOptions();
        asked.Pii.Enabled = true;

        var postures = this.PosturesOver(
            deployment,
            (SyntheticMailOwner.Deployment, asked),
            (SyntheticMailOwner.Another, new OwnerSensitiveContentOptions()));

        // Act
        var current = postures.Current;

        // Assert
        Assert.Equal(
            [SyntheticMailOwner.Deployment, SyntheticMailOwner.Another],
            current.Select(owner => owner.Owner));
        Assert.Equal([SensitiveContentScannerKind.Pii], current[0].Posture.Scanners);
        Assert.Empty(current[1].Posture.Scanners);
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Arrange
        var deployment = new SensitiveContentOptions();
        var servedOwners = new ServedMailOwners();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            null!,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedOwners));
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            deployment,
            null!,
            this.Detectors,
            TimeProvider.System,
            this.permits,
            servedOwners));
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            deployment,
            [],
            null!,
            TimeProvider.System,
            this.permits,
            servedOwners));
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            null!,
            this.permits,
            servedOwners));
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            null!,
            servedOwners));
        Assert.Throws<ArgumentNullException>(() => new OwnerSensitiveContentPostures(
            deployment,
            [],
            this.Detectors,
            TimeProvider.System,
            this.permits,
            null!));
    }

    /// <summary>Builds the roster entry an owner declared in the deployment's own file arrives as.</summary>
    private static ServedMailOwner Serving(MailOwnerId owner, OwnerSensitiveContentOptions sensitiveContent) =>
        new(
            owner,
            owner.Value.ToString(),
            MailOwnerAccountSource.DeploymentSection,
            [],
            sensitiveContent);

    /// <summary>Composes the postures of a deployment whose roster is settled and names exactly these owners.</summary>
    private OwnerSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        params (MailOwnerId Owner, OwnerSensitiveContentOptions SensitiveContent)[] owners)
    {
        var servedOwners = new ServedMailOwners();

        servedOwners.Resolved([.. owners.Select(entry => Serving(entry.Owner, entry.SensitiveContent))]);

        return this.PosturesOver(deployment, servedOwners);
    }

    /// <summary>Composes the postures over a roster the test drives itself, which is how a later write is exercised.</summary>
    private OwnerSensitiveContentPostures PosturesOver(
        SensitiveContentOptions deployment,
        ServedMailOwners servedOwners) => new(
        deployment,
        [SecretsCatalog, PersonalDataCatalog],
        this.Detectors,
        TimeProvider.System,
        this.permits,
        servedOwners);

    /// <summary>
    /// Stands in for the detectors the composition root registered, and counts how often they were asked for. Resolving
    /// them is what constructs a regular-expression corpus and an analyzer client, so the count is the evidence that a
    /// posture nobody reads costs nothing and that two owners asking the same thing pay for one.
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

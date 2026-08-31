// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Egress;

/// <summary>Covers the guard's refusing counterpart: the one that answers whether an act may happen at all.</summary>
public sealed class SensitiveContentEgressScreenTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ScreenAsync_ADeploymentThatScansNothing_StopsNothingAndOpensNoOperation()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();
        var screen = new SensitiveContentEgressScreen(
            FixedSensitiveContentPostures.ScanningNothing(),
            telemetry,
            this.timeProvider);

        // Act
        var refusal = await screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(screen.IsActiveFor(ScanningSensitiveContentEgress.Owner));
        Assert.Null(refusal);
        Assert.Empty(telemetry.Operations);
        Assert.Empty(telemetry.Guarded);
    }

    /// <summary>
    /// A send is stopped for the owner whose record screens for what it carries, and goes through for the owner whose
    /// does not. The owner is stated by the act rather than read off the flow, because a send names whose message it is.
    /// </summary>
    [Fact]
    public async Task ScreenAsync_TwoOwnersOfOneDeployment_StopsTheActForTheOneWhoScreensForIt()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var screen = new SensitiveContentEgressScreen(
            FixedSensitiveContentPostures.Of(
                SensitiveContentPosture.ScanningNothing,
                (SyntheticMailOwner.Deployment, egress.Postures.ForOwner(SyntheticMailOwner.Deployment))),
            new RecordingSensitiveContentEgressTelemetry(),
            this.timeProvider);

        // Act
        var screened = await screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            SyntheticMailOwner.Deployment,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);
        var unscreened = await screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            SyntheticMailOwner.Another,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(screened);
        Assert.Equal(MarkerSensitiveContentScanner.Category, screened.Category);
        Assert.Null(unscreened);
        Assert.True(screen.IsActiveFor(SyntheticMailOwner.Deployment));
        Assert.False(screen.IsActiveFor(SyntheticMailOwner.Another));
    }

    [Fact]
    public async Task ScreenAsync_ADeploymentThatDetectsTheMarkerAndScreensForIt_StopsTheActNamingTheCategory()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(egress.Screen.IsActiveFor(ScanningSensitiveContentEgress.Owner));
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.Equal(MarkerSensitiveContentScanner.Category, refusal.Category);

        var stopped = Assert.Single(egress.Telemetry.Stopped);

        Assert.Equal(SensitiveContentEgressPoint.OutgoingMail, stopped.EgressPoint);
        Assert.Equal(refusal, stopped.Refusal);
    }

    [Fact]
    public async Task ScreenAsync_ATextCarryingNothing_LetsTheActThroughAndReportsWhatItScanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            ["a subject", "an ordinary message"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.Equal(["a subject", "an ordinary message"], egress.Scanner.ScannedTexts);
        Assert.Empty(egress.Telemetry.Stopped);
        Assert.Equal(2, egress.Telemetry.Guarded.Count);

        var operation = Assert.Single(egress.Telemetry.Operations);

        Assert.Equal(2, operation.GuardedTextCount);
        Assert.True(operation.WasCompleted);
        Assert.True(operation.WasClosed);
    }

    [Fact]
    public async Task ScreenAsync_AnEarlierTextThatStopsTheAct_LeavesTheRestUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [$"subject with {Marker}", "the body nobody reached"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal([$"subject with {Marker}"], egress.Scanner.ScannedTexts);
    }

    /// <summary>
    /// The screen is switched on and the scan runs; what the scanner found is simply not a category this deployment
    /// stops an act for. That is the case <c>ScreenOutgoingMailFor</c> exists to make possible — a scanner redacting
    /// everywhere else while a send goes through — so a policy read as stopping everything it can see would refuse
    /// nearly every message a deployment with the personal-data scanner on tried to send.
    /// </summary>
    [Fact]
    public async Task ScreenAsync_AFindingOfACategoryNothingScreensFor_ScansItAndLetsTheActThrough()
    {
        // Arrange
        var scanner = new MarkerSensitiveContentScanner(Marker, SensitiveContentScannerKind.Secrets, this.timeProvider);

        // The deployment screens for a category of the same switched-on scanner that the scanner never reports, so the
        // screen is active, the text is scanned, and the finding reaches the policy and is answered no by it.
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(
                scanner.Scanner,
                [MarkerSensitiveContentScanner.Category, SensitiveContentCategory.Create("PrivateKey")],
                [])]);
        var telemetry = new RecordingSensitiveContentEgressTelemetry();

        using var permits = new SensitiveContentScanConcurrency(plan.Bounds.MaximumConcurrentScans);

        var screen = new SensitiveContentEgressScreen(
            FixedSensitiveContentPostures.ForEveryOwner(
                SensitiveContentPosture.Scanning(
                    [scanner.Scanner],
                    new SensitiveContentRedactor(plan, [scanner], this.timeProvider, permits),
                    SensitiveContentScreeningPolicy.Create(
                        SensitiveContentPlan.Create(
                            SensitiveContentScanBounds.Default,
                            [SensitiveContentScannerPlan.Create(
                                scanner.Scanner,
                                [SensitiveContentCategory.Create("PrivateKey")],
                                [])]),
                        [SensitiveContentScannerKind.Secrets]),
                    SensitiveContentDerivationStamp.Compute(plan, [scanner]))),
            telemetry,
            this.timeProvider);

        // Act
        var refusal = await screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(screen.IsActiveFor(ScanningSensitiveContentEgress.Owner));
        Assert.Null(refusal);
        Assert.Equal([$"the key is {Marker}"], scanner.ScannedTexts);
        Assert.Single(telemetry.Guarded);
        Assert.Empty(telemetry.Stopped);
    }

    [Fact]
    public async Task ScreenAsync_ATextTheCeilingCut_StopsTheActBecauseNothingReadItsRemainder()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(
            Marker,
            this.timeProvider,
            bounds: SensitiveContentScanBounds.Create(
                maximumAnalyzedCharacters: 8,
                TimeSpan.FromSeconds(15),
                maximumConcurrentScans: 4));

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            ["a message far longer than the ceiling analyzes"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.TextExceededScanCeiling, refusal.Reason);
        Assert.Null(refusal.Scanner);
        Assert.Null(refusal.Category);
    }

    [Fact]
    public async Task ScreenAsync_ATextTheCeilingCutThatAlsoCarriesAFinding_StopsTheActForWhatWasFound()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(
            Marker,
            this.timeProvider,
            bounds: SensitiveContentScanBounds.Create(
                maximumAnalyzedCharacters: Marker.Length,
                TimeSpan.FromSeconds(15),
                maximumConcurrentScans: 4));

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [$"{Marker} and a great deal of text after it",],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
        Assert.Equal(MarkerSensitiveContentScanner.Category, refusal.Category);
    }

    [Fact]
    public async Task ScreenAsync_AScannerThatCannotAnswer_RefusesTheActAndReportsTheScanner()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(this.timeProvider);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => egress.Screen.ScreenAsync(
                SensitiveContentEgressPoint.OutgoingMail,
                ScanningSensitiveContentEgress.Owner,
                ["an ordinary message"],
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.Empty(egress.Telemetry.Stopped);

        var recorded = Assert.Single(egress.Telemetry.Refused);

        Assert.Equal(SensitiveContentEgressPoint.OutgoingMail, recorded.EgressPoint);

        var operation = Assert.Single(egress.Telemetry.Operations);

        Assert.True(operation.WasRefused);
        Assert.False(operation.WasCompleted);
    }

    [Fact]
    public async Task ScreenAsync_NoTextsAtAll_StopsNothingAndScansNothing()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            ScanningSensitiveContentEgress.Owner,
            [],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.Empty(egress.Scanner.ScannedTexts);
        Assert.Empty(egress.Telemetry.Operations);
    }
}

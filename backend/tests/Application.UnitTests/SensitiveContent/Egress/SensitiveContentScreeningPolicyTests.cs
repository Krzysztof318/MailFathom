// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Egress;

/// <summary>Covers which of a deployment's findings stop an act rather than being redacted out of a result.</summary>
public sealed class SensitiveContentScreeningPolicyTests
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");
    private static readonly SensitiveContentCategory Person = SensitiveContentCategory.Create("Person");

    private static readonly SensitiveContentDetector Detector =
        SensitiveContentDetector.Create("marker", "2026.08.22");

    [Fact]
    public void RefusesAnything_ADeploymentThatNamedNoScanner_ScreensNothing()
    {
        // Arrange
        var plan = PlanOf(
            (SensitiveContentScannerKind.Secrets, CloudKey),
            (SensitiveContentScannerKind.Pii, Person));

        // Act
        var policy = SensitiveContentScreeningPolicy.Create(plan, []);

        // Assert
        Assert.False(policy.RefusesAnything);
        Assert.Null(policy.StoppedBy(FindingOf(CloudKey)));
    }

    [Fact]
    public void StoppedBy_ACategoryOfANamedScanner_NamesTheScannerThatCoversIt()
    {
        // Arrange
        var plan = PlanOf(
            (SensitiveContentScannerKind.Secrets, CloudKey),
            (SensitiveContentScannerKind.Pii, Person));
        var policy = SensitiveContentScreeningPolicy.Create(plan, [SensitiveContentScannerKind.Secrets]);

        // Act
        var stopped = policy.StoppedBy(FindingOf(CloudKey));

        // Assert
        Assert.True(policy.RefusesAnything);
        Assert.Equal(SensitiveContentScannerKind.Secrets, stopped);
    }

    [Fact]
    public void StoppedBy_ACategoryOfAScannerNobodyNamed_StopsNothing()
    {
        // Arrange
        var plan = PlanOf(
            (SensitiveContentScannerKind.Secrets, CloudKey),
            (SensitiveContentScannerKind.Pii, Person));
        var policy = SensitiveContentScreeningPolicy.Create(plan, [SensitiveContentScannerKind.Secrets]);

        // Act
        var stopped = policy.StoppedBy(FindingOf(Person));

        // Assert
        Assert.Null(stopped);
    }

    [Fact]
    public void StoppedBy_ACategorySpelledDifferently_StopsTheActAllTheSame()
    {
        // Arrange
        var plan = PlanOf((SensitiveContentScannerKind.Secrets, CloudKey));
        var policy = SensitiveContentScreeningPolicy.Create(plan, [SensitiveContentScannerKind.Secrets]);

        // Act
        var stopped = policy.StoppedBy(FindingOf(SensitiveContentCategory.Create("cloudkey")));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, stopped);
    }

    [Fact]
    public void Create_AScannerNamedWhoseSwitchIsOff_ScreensNothingRatherThanFailing()
    {
        // Arrange
        var plan = PlanOf((SensitiveContentScannerKind.Secrets, CloudKey));

        // Act
        var policy = SensitiveContentScreeningPolicy.Create(plan, [SensitiveContentScannerKind.Pii]);

        // Assert
        Assert.False(policy.RefusesAnything);
        Assert.Null(policy.StoppedBy(FindingOf(CloudKey)));
    }

    [Fact]
    public void Create_AScannerNamedTwice_ScreensItsCategoriesOnce()
    {
        // Arrange
        var plan = PlanOf((SensitiveContentScannerKind.Secrets, CloudKey));

        // Act
        var policy = SensitiveContentScreeningPolicy.Create(
            plan,
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Secrets]);

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, policy.StoppedBy(FindingOf(CloudKey)));
    }

    [Fact]
    public void ScreeningNothing_ADeploymentThatScansNothing_StopsNothing()
    {
        // Act
        var policy = SensitiveContentScreeningPolicy.ScreeningNothing();

        // Assert
        Assert.False(policy.RefusesAnything);
        Assert.Null(policy.StoppedBy(FindingOf(CloudKey)));
    }

    private static SensitiveContentPlan PlanOf(
        params (SensitiveContentScannerKind Scanner, SensitiveContentCategory Category)[] scanners) =>
        SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [.. scanners.Select(scanner => SensitiveContentScannerPlan.Create(
                scanner.Scanner,
                [scanner.Category],
                []))]);

    private static SensitiveContentFinding FindingOf(SensitiveContentCategory category) =>
        SensitiveContentFinding.Create(
            SensitiveContentRule.Create(category, "marker"),
            SensitiveContentSpan.Create(0, 4),
            confidence: 1,
            Detector,
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));
}

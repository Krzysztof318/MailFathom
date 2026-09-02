// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent;

/// <summary>Covers what a resolved plan admits, and the shapes that would leave a switched-on scanner finding nothing.</summary>
public sealed class SensitiveContentPlanTests
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");
    private static readonly SensitiveContentCategory PersonName = SensitiveContentCategory.Create("PersonName");

    [Fact]
    public void ScannerPlan_CategoriesAndSuppressions_AreCarriedThrough()
    {
        // Arrange
        var suppressed = SensitiveContentRule.Create(CloudKey, "generic-api-key");

        // Act
        var plan = SensitiveContentScannerPlan.Create(
            SensitiveContentScannerKind.Secrets,
            [CloudKey],
            [suppressed]);

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, plan.Scanner);
        Assert.Equal([CloudKey], plan.Categories);
        Assert.True(plan.Suppresses(suppressed));
        Assert.False(plan.Suppresses(SensitiveContentRule.Create(CloudKey, "aws-access-token")));
    }

    /// <summary>A scanner that is on and looks for nothing is indistinguishable at run time from one that is working.</summary>
    [Fact]
    public void ScannerPlan_NoCategory_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentScannerPlan.Create(
            SensitiveContentScannerKind.Secrets,
            [],
            []));
    }

    [Fact]
    public void ScannerPlan_SameCategoryTwice_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentScannerPlan.Create(
            SensitiveContentScannerKind.Secrets,
            [CloudKey, SensitiveContentCategory.Create("cloudkey")],
            []));
    }

    /// <summary>A suppression describes something inside a category being looked for; one outside them would be a rule nobody would have run.</summary>
    [Fact]
    public void ScannerPlan_SuppressionOutsideTheCategoriesLookedFor_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentScannerPlan.Create(
            SensitiveContentScannerKind.Secrets,
            [CloudKey],
            [SensitiveContentRule.Create(PersonName, "person")]));
    }

    /// <summary>Registration order decides which category names an overlapping redaction, so the plan fixes it instead.</summary>
    [Fact]
    public void Create_Scanners_AreOrderedByScannerRatherThanByConfiguration()
    {
        // Arrange
        var personalData = SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Pii, [PersonName], []);
        var secrets = SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Secrets, [CloudKey], []);

        // Act
        var plan = SensitiveContentPlan.Create(SensitiveContentScanBounds.Default, [personalData, secrets]);

        // Assert
        Assert.Equal([secrets, personalData], plan.Scanners);
    }

    [Fact]
    public void TryGetScanner_SwitchedOnScanner_FindsItsPlan()
    {
        // Arrange
        var secrets = SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Secrets, [CloudKey], []);
        var plan = SensitiveContentPlan.Create(SensitiveContentScanBounds.Default, [secrets]);

        // Act
        var foundSecrets = plan.TryGetScanner(SensitiveContentScannerKind.Secrets, out var secretsPlan);
        var foundPersonalData = plan.TryGetScanner(SensitiveContentScannerKind.Pii, out var personalDataPlan);

        // Assert
        Assert.True(foundSecrets);
        Assert.Same(secrets, secretsPlan);
        Assert.False(foundPersonalData);
        Assert.Null(personalDataPlan);
    }

    /// <summary>A deployment with both switches off composes no plan at all rather than an empty one.</summary>
    [Fact]
    public void Create_NoScanner_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentPlan.Create(SensitiveContentScanBounds.Default, []));
    }

    [Fact]
    public void Create_SameScannerTwice_IsRejected()
    {
        // Arrange
        var secrets = SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Secrets, [CloudKey], []);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [secrets, secrets]));
    }
}

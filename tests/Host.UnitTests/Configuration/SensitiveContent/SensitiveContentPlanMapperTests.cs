// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the resolution from what an operator wrote onto what the scanners actually run.</summary>
public sealed class SensitiveContentPlanMapperTests
{
    private static readonly ISensitiveContentCatalog SecretsCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Secrets,
        [
            StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token", "gcp-api-key"),
            StubSensitiveContentCatalog.Declare("PrivateKey", detectedByDefault: true, "pem-block"),
            StubSensitiveContentCatalog.Declare("GenericHighEntropy", detectedByDefault: false, "entropy"),
        ]);

    private static readonly ISensitiveContentCatalog PersonalDataCatalog = new StubSensitiveContentCatalog(
        SensitiveContentScannerKind.Pii,
        [StubSensitiveContentCatalog.Declare("PersonName", detectedByDefault: true, "person")]);

    /// <summary>An opt-in nobody took composes no plan, which is what leaves the composition root with nothing to register.</summary>
    [Fact]
    public void Map_BothSwitchesOff_ComposesNoPlan()
    {
        // Act
        var plan = SensitiveContentPlanMapper.Map(new SensitiveContentOptions(), [SecretsCatalog]);

        // Assert
        Assert.Null(plan);
    }

    /// <summary>Naming nothing yields the scanner's opinion rather than an empty set.</summary>
    [Fact]
    public void Map_NoCategoryNamed_YieldsTheScannerDefaults()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        var scanner = Assert.Single(plan!.Scanners);
        Assert.Equal(
            ["CloudKey", "PrivateKey"],
            scanner.Categories.Select(category => category.Name));
    }

    /// <summary>A list replaces the defaults rather than adding to them, so what is scanned for is readable from the file alone.</summary>
    [Fact]
    public void Map_CategoriesNamed_ReplaceTheDefaultsRatherThanAddingToThem()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("GenericHighEntropy");

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        var scanner = Assert.Single(plan!.Scanners);
        Assert.Equal(["GenericHighEntropy"], scanner.Categories.Select(category => category.Name));
    }

    /// <summary>The declared spelling is what survives the match, so a placeholder does not depend on how an operator capitalized a name.</summary>
    [Fact]
    public void Map_CategoryNamedWithAnotherCapitalization_ResolvesToTheDeclaredSpelling()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("cloudkey");

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        var scanner = Assert.Single(plan!.Scanners);
        Assert.Equal("CloudKey", Assert.Single(scanner.Categories).Name);
    }

    [Fact]
    public void Map_SuppressionInsideALookedForCategory_SilencesThatRuleAlone()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Suppressions.Add(new SensitiveContentRuleSuppressionOptions
        {
            Category = "CloudKey",
            Rule = "gcp-api-key",
        });

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        var scanner = Assert.Single(plan!.Scanners);
        var cloudKey = scanner.Categories.Single(category => category.Name == "CloudKey");
        Assert.True(scanner.Suppresses(SensitiveContentRule.Create(cloudKey, "gcp-api-key")));
        Assert.False(scanner.Suppresses(SensitiveContentRule.Create(cloudKey, "aws-access-token")));
    }

    /// <summary>Suppressing every rule of a category is not how a category is switched off, and never how one is switched on.</summary>
    [Fact]
    public void Map_SuppressionNamingACategoryNotLookedFor_DoesNotSwitchItOn()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("PrivateKey");
        settings.Secrets.Suppressions.Add(new SensitiveContentRuleSuppressionOptions
        {
            Category = "CloudKey",
            Rule = "aws-access-token",
        });

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        var scanner = Assert.Single(plan!.Scanners);
        Assert.Equal(["PrivateKey"], scanner.Categories.Select(category => category.Name));
        Assert.Empty(scanner.SuppressedRules);
    }

    [Fact]
    public void Map_BothSwitchesOn_PlansBothScanners()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Pii.Enabled = true;

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [PersonalDataCatalog, SecretsCatalog]);

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            plan!.Scanners.Select(scanner => scanner.Scanner));
    }

    [Fact]
    public void Map_ConfiguredBounds_ReachThePlan()
    {
        // Arrange
        var settings = new SensitiveContentOptions
        {
            MaximumAnalyzedCharacters = 4_096,
            ScanTimeout = TimeSpan.FromSeconds(30),
            MaximumConcurrentScans = 2,
        };
        settings.Secrets.Enabled = true;

        // Act
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]);

        // Assert
        Assert.Equal(4_096, plan!.Bounds.MaximumAnalyzedCharacters);
        Assert.Equal(TimeSpan.FromSeconds(30), plan.Bounds.ScanTimeout);
        Assert.Equal(2, plan.Bounds.MaximumConcurrentScans);
    }

    /// <summary>Startup refuses this first; composing a plan around it would replace a named failure with an empty scan.</summary>
    [Fact]
    public void Map_SwitchOnWithNoDetectorRegistered_IsRefused()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => SensitiveContentPlanMapper.Map(settings, [SecretsCatalog]));
    }

    [Fact]
    public void Map_WithoutSettingsOrCatalogs_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlanMapper.Map(null!, [SecretsCatalog]));
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlanMapper.Map(new SensitiveContentOptions(), null!));
    }
}

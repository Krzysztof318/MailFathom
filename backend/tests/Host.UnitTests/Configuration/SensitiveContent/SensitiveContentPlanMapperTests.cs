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
        var plan = SensitiveContentPlanMapper.Map(new SensitiveContentOptions(), [SecretsCatalog], []);

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [PersonalDataCatalog, SecretsCatalog], SwitchedOn(settings));

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
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings));

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
        Assert.Throws<InvalidOperationException>(() => SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings)));
    }

    [Fact]
    public void Map_WithoutSettingsOrCatalogs_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlanMapper.Map(null!, [SecretsCatalog], []));
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlanMapper.Map(new SensitiveContentOptions(), null!, []));
    }

    /// <summary>The scanner and its startup probe read one profile, so every configured value reaches both or neither.</summary>
    [Fact]
    public void MapAnalyzerProfile_AConfiguredAnalyzer_CarriesItsAddressLanguagesAndConfidenceFloor()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add("pl");
        settings.PersonalDataAnalyzer.Languages.Add("de");
        settings.PersonalDataAnalyzer.MinimumConfidence = 0.7;

        // Act
        var profile = SensitiveContentPlanMapper.MapAnalyzerProfile(settings);

        // Assert
        Assert.Equal("http://presidio-analyzer:3000/", profile.Endpoint.ToString());
        Assert.Equal(["de", "pl"], profile.Languages);
        Assert.Equal(0.7, profile.MinimumConfidence);
    }

    /// <summary>
    /// An unnamed list means the language the shipped analyzer image serves, exactly as an unnamed category list means the
    /// scanner's default categories — the binder adds to a bound collection rather than replacing it, so the default
    /// cannot live on the property without becoming a language nobody can remove.
    /// </summary>
    [Fact]
    public void MapAnalyzerProfile_AnAnalyzerNoLanguageWasStatedFor_IsAskedInTheShippedImagesOwn()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        // Act
        var profile = SensitiveContentPlanMapper.MapAnalyzerProfile(settings);

        // Assert
        Assert.Equal(["en"], profile.Languages);
    }

    /// <summary>An analyzer nobody tuned has to arrive with the floor that keeps its sub-0.1 guesses out of the text.</summary>
    [Fact]
    public void MapAnalyzerProfile_AnAnalyzerNoConfidenceWasStatedFor_CarriesTheDefaultFloor()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        // Act
        var profile = SensitiveContentPlanMapper.MapAnalyzerProfile(settings);

        // Assert
        Assert.Equal(0.4, profile.MinimumConfidence);
    }

    /// <summary>
    /// The profile follows the address rather than the switch, because an owner may switch the scanner on for their own
    /// mail while the deployment left it off, and the client it would then reach is registered before any roster exists.
    /// </summary>
    [Fact]
    public void MapAnalyzerProfile_ADeploymentThatStoodTheAnalyzerUpWithTheSwitchOff_ComposesTheProfileAnyway()
    {
        // Arrange
        var scannerOff = new SensitiveContentOptions();
        scannerOff.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        // Act
        var profile = SensitiveContentPlanMapper.MapAnalyzerProfile(scannerOff);

        // Assert
        Assert.Equal(new Uri("http://presidio-analyzer:3000"), profile.Endpoint);
    }

    /// <summary>Without an address there is nothing to compose, whoever asked for the scanner.</summary>
    [Fact]
    public void MapAnalyzerProfile_ADeploymentWithNoAnalyzerAddress_IsRefused()
    {
        // Arrange
        var noAddress = new SensitiveContentOptions();
        noAddress.Pii.Enabled = true;

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => SensitiveContentPlanMapper.MapAnalyzerProfile(noAddress));
        Assert.Throws<ArgumentNullException>(() => SensitiveContentPlanMapper.MapAnalyzerProfile(null!));
    }

    /// <summary>
    /// A deployment that switched a scanner on and said nothing about outgoing mail screens for secrets, because a key
    /// in a message is the case this feature exists for and no ordinary correspondence carries one.
    /// </summary>
    [Fact]
    public void MapScreeningPolicy_ADeploymentThatNamedNothing_ScreensForSecrets()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Pii.Enabled = true;
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog, PersonalDataCatalog], SwitchedOn(settings))!;

        // Act
        var policy = SensitiveContentPlanMapper.MapScreeningPolicy(plan, SensitiveContentPlanMapper.ScreeningScannersOf(settings));

        // Assert
        Assert.True(policy.RefusesAnything);
        Assert.Equal(SensitiveContentScannerKind.Secrets, policy.StoppedBy(FindingOf("CloudKey")));
        Assert.Null(policy.StoppedBy(FindingOf("PersonName")));
    }

    /// <summary>
    /// Personal data is what ordinary correspondence is made of, so screening for it stops nearly every message and is
    /// never reached without the operator naming it.
    /// </summary>
    [Fact]
    public void MapScreeningPolicy_ADeploymentThatNamedBothScanners_ScreensForBoth()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = ["Secrets", "pii"] };
        settings.Secrets.Enabled = true;
        settings.Pii.Enabled = true;
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog, PersonalDataCatalog], SwitchedOn(settings))!;

        // Act
        var policy = SensitiveContentPlanMapper.MapScreeningPolicy(plan, SensitiveContentPlanMapper.ScreeningScannersOf(settings));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, policy.StoppedBy(FindingOf("CloudKey")));
        Assert.Equal(SensitiveContentScannerKind.Pii, policy.StoppedBy(FindingOf("PersonName")));
    }

    /// <summary>Naming nothing is how an operator keeps a scanner redacting without ever stopping a send.</summary>
    [Fact]
    public void MapScreeningPolicy_ADeploymentThatNamedNoScanner_ScreensNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = [] };
        settings.Secrets.Enabled = true;
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog], SwitchedOn(settings))!;

        // Act
        var policy = SensitiveContentPlanMapper.MapScreeningPolicy(plan, SensitiveContentPlanMapper.ScreeningScannersOf(settings));

        // Assert
        Assert.False(policy.RefusesAnything);
        Assert.Null(policy.StoppedBy(FindingOf("CloudKey")));
    }

    /// <summary>A scanner named for screening but never switched on has nothing to screen with, and stops nothing.</summary>
    [Fact]
    public void MapScreeningPolicy_AScannerNamedWhoseSwitchIsOff_ScreensNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = ["Pii"] };
        settings.Secrets.Enabled = true;
        var plan = SensitiveContentPlanMapper.Map(settings, [SecretsCatalog, PersonalDataCatalog], SwitchedOn(settings))!;

        // Act
        var policy = SensitiveContentPlanMapper.MapScreeningPolicy(plan, SensitiveContentPlanMapper.ScreeningScannersOf(settings));

        // Assert
        Assert.False(policy.RefusesAnything);
    }

    /// <summary>The scanners this deployment switched on for every owner, which is what its own posture runs.</summary>
    /// <remarks>
    /// Which scanners run is an argument to the mapper rather than a reading of the section, because an owner's record
    /// can switch one on that the deployment left off. What these tests are about is everything else the section
    /// decides, so each of them passes the deployment's own answer.
    /// </remarks>
    private static IReadOnlyList<SensitiveContentScannerKind> SwitchedOn(SensitiveContentOptions settings) =>
        [.. Enum.GetValues<SensitiveContentScannerKind>().Where(scanner => settings.For(scanner).Enabled)];

    private static SensitiveContentFinding FindingOf(string category) =>
        SensitiveContentFinding.Create(
            SensitiveContentRule.Create(SensitiveContentCategory.Create(category), $"{category}-rule"),
            SensitiveContentSpan.Create(0, length: 4),
            confidence: 1,
            SensitiveContentDetector.Create("test", "1"),
            new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the startup refusals that stop a mistyped configuration from reading as a protection that is on.</summary>
public sealed class SensitiveContentDeclarationRulesTests
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

    /// <summary>Both switches off is the product as it stands, so nothing about the section is judged at all.</summary>
    [Fact]
    public void FindDeclarationErrors_BothSwitchesOff_ReportsNothingEvenWithNoScannerRegistered()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Categories.Add("NoSuchCategory");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, []);

        // Assert
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FindDeclarationErrors_EachSupportedSwitchCombination_ReportsNothing(bool secrets, bool personalData)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = secrets;
        settings.Pii.Enabled = personalData;

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(
            settings,
            [SecretsCatalog, PersonalDataCatalog]);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A scanner that cannot run must stop the service rather than surface later as content nobody scanned.</summary>
    [Fact]
    public void FindDeclarationErrors_SwitchOnWithNoDetectorRegistered_NamesTheScanner()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("SensitiveContent:Pii", error.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("registers no detector", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>The binder would drop a name it cannot bind and start anyway, which is the failure this refusal exists to prevent.</summary>
    [Fact]
    public void FindDeclarationErrors_UnknownCategoryName_NamesTheValueAndWhatIsDetected()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKey");
        settings.Secrets.Categories.Add("PrivateKeys");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("'PrivateKeys'", error.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("'PrivateKey'", error.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:Secrets:Categories:1", error.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDeclarationErrors_UnknownSuppressedRule_NamesTheRuleAndItsCategory()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Suppressions.Add(new SensitiveContentRuleSuppressionOptions
        {
            Category = "CloudKey",
            Rule = "aws-access-tokens",
        });

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("'aws-access-tokens'", error.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("'CloudKey'", error.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDeclarationErrors_SuppressionNamingAnUnknownCategory_NamesTheValue()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Suppressions.Add(new SensitiveContentRuleSuppressionOptions
        {
            Category = "PersonName",
            Rule = "person",
        });

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("SensitiveContent:Secrets:Suppressions:0:Category", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>A category switched off with a suppression left beside it says nothing untrue about what is scanned for.</summary>
    [Fact]
    public void FindDeclarationErrors_SuppressionInsideACategoryNotLookedFor_IsAccepted()
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
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDeclarationErrors_BlankCategoryEntry_NamesItsPosition()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKey");
        settings.Secrets.Categories.Add("  ");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("SensitiveContent:Secrets:Categories:1", error.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDeclarationErrors_SuppressionMissingHalfOfItsPair_NamesItsPosition()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Suppressions.Add(new SensitiveContentRuleSuppressionOptions { Category = "CloudKey" });

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("must name both a category and a rule", error.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDeclarationErrors_SameCategoryNamedTwice_ReportsIt()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKey");
        settings.Secrets.Categories.Add("cloudkey");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("2 times", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>Two detectors for one switch leave which categories it looks for undecidable.</summary>
    [Fact]
    public void FindDeclarationErrors_TwoDetectorsForOneSwitch_ReportsIt()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(
            settings,
            [SecretsCatalog, SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("2 detectors", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>A scanner that is on and looks for nothing reads at run time exactly like one that is working.</summary>
    [Fact]
    public void FindDeclarationErrors_ScannerWithNoDefaultCategory_ReportsThatItWouldFindNothing()
    {
        // Arrange
        var catalog = new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("GenericHighEntropy", detectedByDefault: false, "entropy")]);
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [catalog]);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("find nothing", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>An operator who wrote two mistakes reads both rather than fixing one and restarting into the other.</summary>
    [Fact]
    public void FindDeclarationErrors_SeveralProblems_AreAllReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKeys");
        settings.Pii.Enabled = true;
        settings.Pii.Categories.Add("PersonNames");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(
            settings,
            [SecretsCatalog, PersonalDataCatalog]);

        // Assert
        Assert.Equal(4, errors.Count);
    }

    /// <summary>
    /// An owner's own record may switch a provided scanner on for their own mail, so what an operator wrote under a
    /// switch that is off is not a comment. Left unjudged it would pass every start and then throw out of the posture
    /// composition the moment somebody opted in, which stops scanning for the whole deployment rather than for them.
    /// </summary>
    [Fact]
    public void FindDeclarationErrors_AnUnknownCategoryUnderASwitchThatIsOff_IsStillRefused()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Categories.Add("CloudKey");
        settings.Secrets.Categories.Add("PrivateKeys");

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("SensitiveContent:Secrets:Categories:1", error.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>A scanner nobody wrote anything under, and nobody switched on, is still nothing to refuse a start over.</summary>
    [Fact]
    public void FindDeclarationErrors_AProvidedScannerNobodyWroteAnythingUnder_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();

        // Act
        var errors = SensitiveContentDeclarationRules.FindDeclarationErrors(settings, [SecretsCatalog]);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDeclarationErrors_WithoutSettingsOrCatalogs_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            SensitiveContentDeclarationRules.FindDeclarationErrors(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            SensitiveContentDeclarationRules.FindDeclarationErrors(new SensitiveContentOptions(), null!));
    }
}

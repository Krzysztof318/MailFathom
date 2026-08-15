// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.SensitiveContent;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers the section's own defaults and bounds, and the validator the options framework runs at startup.</summary>
public sealed class SensitiveContentOptionsTests
{
    /// <summary>The ordinary deployment scans nothing, so an absent section has to be a working configuration.</summary>
    [Fact]
    public void Defaults_BothScanners_AreOff()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.False(settings.Secrets.Enabled);
        Assert.False(settings.Pii.Enabled);
        Assert.False(settings.IsAnyScannerEnabled);
    }

    [Fact]
    public void Defaults_Bounds_AreTheOnesTheApplicationDocuments()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.Equal(SensitiveContentScanBounds.Default.MaximumAnalyzedCharacters, settings.MaximumAnalyzedCharacters);
        Assert.Equal(SensitiveContentScanBounds.Default.ScanTimeout, settings.ScanTimeout);
        Assert.Equal(SensitiveContentScanBounds.Default.MaximumConcurrentScans, settings.MaximumConcurrentScans);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IsAnyScannerEnabled_EitherSwitchOn_IsTrue(bool secrets, bool personalData)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = secrets;
        settings.Pii.Enabled = personalData;

        // Act, Assert
        Assert.True(settings.IsAnyScannerEnabled);
    }

    [Fact]
    public void For_EachScanner_ReadsItsOwnSwitch()
    {
        // Arrange
        var settings = new SensitiveContentOptions();

        // Act, Assert
        Assert.Same(settings.Secrets, settings.For(SensitiveContentScannerKind.Secrets));
        Assert.Same(settings.Pii, settings.For(SensitiveContentScannerKind.Pii));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.For((SensitiveContentScannerKind)7));
    }

    /// <summary>A budget below a second refuses ordinary mail on a busy machine, and one measured in minutes is a stall.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(121)]
    public void Validate_ScanTimeoutOutsideItsRange_IsReported(int seconds)
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScanTimeout = TimeSpan.FromSeconds(seconds) };

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("SensitiveContent:ScanTimeout", result.ErrorMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ScanTimeoutInsideItsRange_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScanTimeout = TimeSpan.FromSeconds(45) };

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// The personal-data scanner reaches an analyzer and fails closed without one, so a deployment that switched it on and
    /// stated no address would refuse every read, derived write, and egress it guards while its own file read as protection
    /// in force.
    /// </summary>
    [Fact]
    public void Validate_PersonalDataScannerOnWithNoAnalyzerAddress_IsReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains(
            "SensitiveContent:PersonalDataAnalyzer:Endpoint",
            result.ErrorMessage!,
            StringComparison.Ordinal);
    }

    /// <summary>An address no request can be composed from is worth refusing at startup rather than at the first guarded read.</summary>
    /// <remarks>
    /// The refusal must also not quote what it was given. A missing scheme is the commonest way to reach this branch, so the
    /// value would be the analyzer's own host name and this message goes to a startup log; each row therefore states the
    /// part of its own value that must not appear, so that an edit interpolating the value back fails here rather than in a
    /// deployment. The rows deliberately name an address other than the one the message offers as an example, because a
    /// value that was a substring of that example could not be distinguished from it.
    /// </remarks>
    [Theory]
    [InlineData("mail-analyzer.internal:3000", "mail-analyzer.internal")]
    [InlineData("/private/analyzer", "/private/analyzer")]
    [InlineData("ftp://mail-analyzer.internal:3000", "mail-analyzer.internal")]
    public void Validate_AnalyzerAddressThatCarriesNoHttpRequest_IsReportedWithoutQuotingIt(
        string endpoint,
        string addressThatMustNotAppear)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = endpoint;

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("not an absolute http or https address", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(addressThatMustNotAppear, result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>Each code reaches a query argument and the detector revision every finding carries.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("eng")]
    public void Validate_AnalyzerLanguageThatIsNotATwoLetterCode_IsReported(string language)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add(language);

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("two-letter lowercase language code", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>One malformed entry beside well-formed ones is still a request the analyzer cannot be asked.</summary>
    [Fact]
    public void Validate_AnalyzerLanguagesWithOneMalformedEntry_IsReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add("en");
        settings.PersonalDataAnalyzer.Languages.Add("polish");

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("polish", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each language is another request inside the one budget a scan is allowed, and the revision every finding carries
    /// names them all — so the ceiling is refused here, where the message can name the key an operator wrote.
    /// </summary>
    [Fact]
    public void Validate_MoreAnalyzerLanguagesThanAScanAsksIn_IsReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        foreach (var language in Enumerable
            .Range(0, PersonalDataAnalyzerProfile.MaximumLanguages + 1)
            .Select(index => $"{(char)('a' + (index / 26))}{(char)('a' + (index % 26))}"))
        {
            settings.PersonalDataAnalyzer.Languages.Add(language);
        }

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("at most 8 are asked for", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>A mixed mailbox names every language its correspondence carries, and nothing about that is a configuration error.</summary>
    [Fact]
    public void Validate_SeveralWellFormedAnalyzerLanguages_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add("en");
        settings.PersonalDataAnalyzer.Languages.Add("pl");

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// A range attribute on the block would enforce nothing, because the options framework reads the annotations of the bound
    /// root and never descends into it — so the bound is checked here, at both ends and on a value that is no number at all.
    /// </summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Validate_AnalyzerConfidenceFloorOutsideZeroToOne_IsReported(double minimumConfidence)
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.MinimumConfidence = minimumConfidence;

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("share of certainty between 0 and 1", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>An address left behind under a scanner nobody runs describes no protection, so refusing over it refuses over a comment.</summary>
    [Fact]
    public void Validate_PersonalDataScannerOff_JudgesTheAnalyzerBlockNotAtAll()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.PersonalDataAnalyzer.Languages.Add("not a language");
        settings.PersonalDataAnalyzer.MinimumConfidence = 12;

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Validate_PersonalDataScannerOnWithAUsableAnalyzer_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// The floor is a default rather than an opt-in, because a deployment that states none must not receive zero. The
    /// languages are empty here rather than defaulted, because the binder adds to a bound collection instead of replacing
    /// it — a default written into the property would be a language an operator could never remove.
    /// </summary>
    [Fact]
    public void Defaults_AnalyzerBlock_NamesNoAddressNoLanguageAndKeepsAFloor()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.Null(settings.PersonalDataAnalyzer.Endpoint);
        Assert.Empty(settings.PersonalDataAnalyzer.Languages);
        Assert.Equal(0.4, settings.PersonalDataAnalyzer.MinimumConfidence);
    }

    [Fact]
    public void CatalogValidator_ConfigurationNamingSomethingNoScannerDetects_FailsStartup()
    {
        // Arrange
        var catalog = new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.Secrets.Categories.Add("CloudKeys");
        var validator = new SensitiveContentCatalogValidator([catalog]);

        // Act
        var result = validator.Validate(SensitiveContentOptions.SectionName, settings);

        // Assert
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("'CloudKeys'", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogValidator_UsableConfiguration_Succeeds()
    {
        // Arrange
        var catalog = new StubSensitiveContentCatalog(
            SensitiveContentScannerKind.Secrets,
            [StubSensitiveContentCatalog.Declare("CloudKey", detectedByDefault: true, "aws-access-token")]);
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        var validator = new SensitiveContentCatalogValidator([catalog]);

        // Act
        var result = validator.Validate(SensitiveContentOptions.SectionName, settings);

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void CatalogValidator_WithoutOptions_IsRejected()
    {
        // Arrange
        var validator = new SensitiveContentCatalogValidator([]);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => validator.Validate(SensitiveContentOptions.SectionName, null!));
    }
}

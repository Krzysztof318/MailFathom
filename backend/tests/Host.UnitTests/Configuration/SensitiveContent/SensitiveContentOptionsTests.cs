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

    /// <summary>The secrets scanner runs inside this process, so every deployment can serve an owner who asks for it.</summary>
    [Fact]
    public void ProvidedScanners_ADeploymentWithNoAnalyzerAddress_ProvidesTheSecretsScannerAlone()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], settings.ProvidedScanners);
        Assert.False(settings.ProvidesPersonalDataScanner);
    }

    /// <summary>Standing the analyzer up is what makes the personal-data scanner available, whoever switches it on.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProvidedScanners_AConfiguredAnalyzer_ProvidesBothWhicheverWayTheSwitchReads(bool switchedOn)
    {
        // Arrange
        var settings = new SensitiveContentOptions { PersonalDataAnalyzer = { Endpoint = "http://analyzer:3000" } };
        settings.Pii.Enabled = switchedOn;

        // Act, Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            settings.ProvidedScanners);
        Assert.True(settings.ProvidesPersonalDataScanner);
    }

    /// <summary>An address no request could be composed from provides nothing, so a record asking for that scanner is refused.</summary>
    [Theory]
    [InlineData("presidio-analyzer:3000")]
    [InlineData("ftp://analyzer:3000")]
    [InlineData("   ")]
    public void ProvidedScanners_AnAddressNothingCouldBeAskedAt_ProvidesTheSecretsScannerAlone(string endpoint)
    {
        // Arrange
        var settings = new SensitiveContentOptions { PersonalDataAnalyzer = { Endpoint = endpoint } };

        // Act, Assert
        Assert.Equal([SensitiveContentScannerKind.Secrets], settings.ProvidedScanners);
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

    /// <summary>
    /// One malformed entry beside well-formed ones is still a request the analyzer cannot be asked, and the message names
    /// that entry alone — a list quoting the valid code beside the invalid one leaves an operator comparing the two with
    /// nothing saying which is which.
    /// </summary>
    [Fact]
    public void Validate_AnalyzerLanguagesWithOneMalformedEntry_IsReportedQuotingOnlyThatEntry()
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
        Assert.Contains("'polish'", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("'en'", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// An address alone stands the analyzer up, for the owners who may switch the scanner on for their own mail, so the
    /// block beside it is judged whether or not this deployment scans with it. Left unjudged, the profile the
    /// composition root builds from these keys would throw out of the first posture that resolved it, taking every
    /// scanning path down with nothing naming the key that did it.
    /// </summary>
    [Fact]
    public void Validate_AMalformedAnalyzerLanguageUnderAScannerOnlyOwnersRun_IsReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Secrets.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add("polish");

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        Assert.False(settings.Pii.Enabled);
        Assert.True(settings.ProvidesPersonalDataScanner);

        var result = Assert.Single(results);
        Assert.Contains("'polish'", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// An address nothing could be built from under a switched-off scanner stands nothing up and describes no
    /// protection, so it is left where it is rather than refusing a start over a setting that decides nothing.
    /// </summary>
    [Fact]
    public void Validate_AnUnusableAnalyzerAddressUnderASwitchedOffScanner_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.PersonalDataAnalyzer.Endpoint = "presidio-analyzer:3000";
        settings.PersonalDataAnalyzer.Languages.Add("polish");

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        Assert.False(settings.ProvidesPersonalDataScanner);
        Assert.Empty(results);
    }

    /// <summary>
    /// The ceiling is counted after deduplication, as the profile counts it. A repeat is one language asked once, so a
    /// list an operator produced by merging two configuration sources must not be refused for a length the analyzer
    /// never sees.
    /// </summary>
    [Fact]
    public void Validate_MoreRawEntriesThanTheCeilingButNoMoreDistinctLanguages_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        foreach (var language in LanguageCodes(PersonalDataAnalyzerProfile.MaximumLanguages))
        {
            settings.PersonalDataAnalyzer.Languages.Add(language);
        }

        settings.PersonalDataAnalyzer.Languages.Add(settings.PersonalDataAnalyzer.Languages[0]);

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The ceiling is inclusive, so a deployment naming exactly as many languages as are asked for starts.</summary>
    [Fact]
    public void Validate_ExactlyAsManyAnalyzerLanguagesAsAreAskedFor_ReportsNothing()
    {
        // Arrange
        var settings = new SensitiveContentOptions();
        settings.Pii.Enabled = true;
        settings.PersonalDataAnalyzer.Endpoint = "http://presidio-analyzer:3000";

        foreach (var language in LanguageCodes(PersonalDataAnalyzerProfile.MaximumLanguages))
        {
            settings.PersonalDataAnalyzer.Languages.Add(language);
        }

        // Act
        var results = settings.Validate(new ValidationContext(settings));

        // Assert
        Assert.Empty(results);
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

        foreach (var language in LanguageCodes(PersonalDataAnalyzerProfile.MaximumLanguages + 1))
        {
            settings.PersonalDataAnalyzer.Languages.Add(language);
        }

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("at most 8 are asked for", result.ErrorMessage!, StringComparison.Ordinal);
    }

    /// <summary>Distinct well-formed codes, so a test about a count is not also a test about the grammar.</summary>
    private static IEnumerable<string> LanguageCodes(int count) => Enumerable
        .Range(0, count)
        .Select(index => $"{(char)('a' + (index / 26))}{(char)('a' + (index % 26))}");

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

    /// <summary>An absent key is what the default reads from, so it may not be an empty list the mapper cannot tell apart.</summary>
    [Fact]
    public void Defaults_ScreenOutgoingMailFor_NamesNothingAtAll()
    {
        // Act
        var settings = new SensitiveContentOptions();

        // Assert
        Assert.Null(settings.ScreenOutgoingMailFor);
    }

    /// <summary>A scanner spelled wrongly reads as protection in force and screens nothing, so startup refuses it.</summary>
    [Fact]
    public void Validate_ScreenOutgoingMailForNamingSomethingThatIsNotAScanner_IsReported()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = ["Secrets", "Secret"] };

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(SensitiveContentOptions.ScreenOutgoingMailFor))
                && result.ErrorMessage!.Contains("'Secret'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Secrets")]
    [InlineData("secrets")]
    [InlineData("PII")]
    public void Validate_ScreenOutgoingMailForNamingAScannerInAnySpelling_IsAccepted(string named)
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = [named] };

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        Assert.DoesNotContain(
            results,
            result => result.MemberNames.Contains(nameof(SensitiveContentOptions.ScreenOutgoingMailFor)));
    }

    /// <summary>Naming nothing is how an operator switches outgoing-mail screening off, rather than a list to refuse.</summary>
    [Fact]
    public void Validate_ScreenOutgoingMailForNamingNoScanner_IsAccepted()
    {
        // Arrange
        var settings = new SensitiveContentOptions { ScreenOutgoingMailFor = [] };

        // Act
        var results = settings.Validate(new ValidationContext(settings)).ToArray();

        // Assert
        Assert.DoesNotContain(
            results,
            result => result.MemberNames.Contains(nameof(SensitiveContentOptions.ScreenOutgoingMailFor)));
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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers what a configured analyzer address is turned into before anything is sent to it.</summary>
public sealed class PersonalDataAnalyzerProfileTests
{
    /// <summary>
    /// A base address that does not end in a slash loses its last segment when a route resolves against it, so an analyzer
    /// behind a reverse-proxy path would be asked somewhere else entirely — a request that arrives and answers something
    /// other than an analysis.
    /// </summary>
    [Theory]
    [InlineData("http://presidio-analyzer:3000", "http://presidio-analyzer:3000/")]
    [InlineData("https://gateway.example.test/presidio", "https://gateway.example.test/presidio/")]
    [InlineData("https://gateway.example.test/presidio/", "https://gateway.example.test/presidio/")]
    public void Create_Endpoint_IsNormalizedIntoAUsableBaseAddress(string configured, string expected)
    {
        // Arrange
        var endpoint = new Uri(configured, UriKind.Absolute);

        // Act
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, ["en"], 0.3);

        // Assert
        Assert.Equal(expected, profile.Endpoint.ToString());
        Assert.Equal(new Uri(profile.Endpoint, "analyze").ToString(), $"{expected}analyze");
    }

    [Theory]
    [InlineData("ftp://presidio-analyzer:3000")]
    [InlineData("file:///analyzer")]
    public void Create_AddressThatCarriesNoHttpRequest_IsRefused(string configured)
    {
        // Arrange
        var endpoint = new Uri(configured, UriKind.Absolute);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => PersonalDataAnalyzerProfile.Create(endpoint, ["en"], 0.3));

        // Assert
        Assert.Equal("endpoint", failure.ParamName);
    }

    /// <summary>The code reaches a query argument and the detector revision every finding carries, so its grammar is narrow.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("EN")]
    [InlineData("eng")]
    [InlineData("e")]
    [InlineData("e n")]
    public void Create_LanguageThatIsNotATwoLetterCode_IsRefused(string language)
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => PersonalDataAnalyzerProfile.Create(endpoint, [language], 0.3));

        // Assert
        Assert.Equal("languages", failure.ParamName);
    }

    /// <summary>A well-formed language beside a malformed one is still a deployment asking a question nothing answers.</summary>
    [Fact]
    public void Create_OneMalformedLanguageAmongSeveral_IsRefused()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => PersonalDataAnalyzerProfile.Create(endpoint, ["en", "polish", "pl"], 0.3));

        // Assert
        Assert.Equal("languages", failure.ParamName);
        Assert.Contains("'polish'", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A configuration source can bind a null into a list, and a hole in the set has to be refused where the message names
    /// the language rather than where deduplication refuses to hash it.
    /// </summary>
    [Fact]
    public void Create_ANullAmongTheLanguages_IsRefused()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);
        var languages = new[] { "en", null! };

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => PersonalDataAnalyzerProfile.Create(endpoint, languages, 0.3));

        // Assert
        Assert.Equal("languages", failure.ParamName);
    }

    /// <summary>Every request states a language, so a deployment asking in none would scan every category and find nothing.</summary>
    [Fact]
    public void Create_NoLanguageAtAll_IsRefused()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => PersonalDataAnalyzerProfile.Create(endpoint, [], 0.3));

        // Assert
        Assert.Equal("languages", failure.ParamName);
    }

    /// <summary>Each language is another request inside one scan's budget, and the revision has to name them all.</summary>
    [Fact]
    public void Create_MoreLanguagesThanAScanAsksIn_IsRefused()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);
        var languages = Enumerable
            .Range(0, PersonalDataAnalyzerProfile.MaximumLanguages + 1)
            .Select(index => $"{(char)('a' + (index / 26))}{(char)('a' + (index % 26))}")
            .ToArray();

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => PersonalDataAnalyzerProfile.Create(endpoint, languages, 0.3));

        // Assert
        Assert.Equal("languages", failure.ParamName);
    }

    /// <summary>The order is the operator's typing rather than a decision, so two deployments that named the same set match.</summary>
    [Fact]
    public void Create_LanguagesInAnyOrderWithRepeats_AreCarriedAsOneOrderedSet()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var written = PersonalDataAnalyzerProfile.Create(endpoint, ["pl", "en", "pl"], 0.3);
        var writtenOtherwise = PersonalDataAnalyzerProfile.Create(endpoint, ["en", "pl"], 0.3);

        // Assert
        Assert.Equal(["en", "pl"], written.Languages);
        Assert.Equal(written.Detector.Revision, writtenOtherwise.Detector.Revision);
    }

    /// <summary>A floor outside the analyzer's own scale would be sent as one, and neither end of it means anything there.</summary>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Create_ConfidenceFloorThatIsNotAShareOfCertainty_IsRefused(double minimumConfidence)
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => PersonalDataAnalyzerProfile.Create(endpoint, ["en"], minimumConfidence));

        // Assert
        Assert.Equal("minimumConfidence", failure.ParamName);
    }

    /// <summary>Both ends are usable settings — everything the analyzer fired on, and only what it was certain of.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.3)]
    [InlineData(1)]
    public void Create_ConfidenceFloorOnTheAnalyzerScale_IsCarriedAsConfigured(double minimumConfidence)
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, ["en"], minimumConfidence);

        // Assert
        Assert.Equal(minimumConfidence, profile.MinimumConfidence);
    }

    /// <summary>Two deployments asking in different languages produce different findings, so the revision has to say which.</summary>
    [Fact]
    public void Create_Detector_CarriesTheLanguageAndTheFloorBesideTheMappingRevision()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, ["de"], 0.3);

        // Assert
        Assert.Equal(
            $"presidio+entities.{PresidioEntityCorpus.MappingRevision}+lang.de+floor.0.3",
            profile.Detector.Revision);
    }

    /// <summary>Adding a language changes what a scan can find, so what was derived before it is not comparable.</summary>
    [Fact]
    public void Create_Detector_NamesEveryLanguageTheTextWasAskedAbout()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, ["pl", "en"], 0.3);

        // Assert
        Assert.Equal(
            $"presidio+entities.{PresidioEntityCorpus.MappingRevision}+lang.en.pl+floor.0.3",
            profile.Detector.Revision);
    }

    /// <summary>The floor decides which regions are replaced, so what was derived under another one is not comparable.</summary>
    /// <remarks>
    /// A derived row's stamp is computed from this revision. An operator lowering the floor to catch more personal data
    /// would otherwise leave every already-indexed message holding what this deployment now redacts, while the startup
    /// report said the mailbox was current — the analyzer never reports below the floor, so nothing downstream could
    /// tell the two apart.
    /// </remarks>
    [Fact]
    public void Create_ADifferentConfidenceFloor_ProducesADifferentRevision()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var strict = PersonalDataAnalyzerProfile.Create(endpoint, ["en"], 0.85);
        var permissive = PersonalDataAnalyzerProfile.Create(endpoint, ["en"], 0.4);

        // Assert
        Assert.NotEqual(strict.Detector.Revision, permissive.Detector.Revision);
    }
}

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
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, "en", 0.3);

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
        var failure = Assert.Throws<ArgumentException>(() => PersonalDataAnalyzerProfile.Create(endpoint, "en", 0.3));

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
        var failure = Assert.Throws<ArgumentException>(() => PersonalDataAnalyzerProfile.Create(endpoint, language, 0.3));

        // Assert
        Assert.Equal("language", failure.ParamName);
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
            () => PersonalDataAnalyzerProfile.Create(endpoint, "en", minimumConfidence));

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
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, "en", minimumConfidence);

        // Assert
        Assert.Equal(minimumConfidence, profile.MinimumConfidence);
    }

    /// <summary>
    /// The revision names how detection is performed, and the floor names which of its results this deployment wants — the
    /// same distinction that keeps the configured categories out of it. Two deployments that redact differently over one
    /// revision are expected; two that asked a different detector are not.
    /// </summary>
    [Fact]
    public void Create_ConfidenceFloor_IsNotPartOfTheDetectorRevision()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var permissive = PersonalDataAnalyzerProfile.Create(endpoint, "en", 0.1);
        var strict = PersonalDataAnalyzerProfile.Create(endpoint, "en", 0.9);

        // Assert
        Assert.Equal(permissive.Detector, strict.Detector);
    }

    /// <summary>Two deployments asking in different languages produce different findings, so the revision has to say which.</summary>
    [Fact]
    public void Create_Detector_CarriesTheLanguageBesideTheMappingRevision()
    {
        // Arrange
        var endpoint = new Uri("http://presidio-analyzer:3000", UriKind.Absolute);

        // Act
        var profile = PersonalDataAnalyzerProfile.Create(endpoint, "de", 0.3);

        // Assert
        Assert.Equal($"presidio+entities.{PresidioEntityCorpus.MappingRevision}+lang.de", profile.Detector.Revision);
    }
}

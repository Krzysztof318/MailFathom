// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Answering;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Answering;

/// <summary>Covers what an answering declaration has to say before an instance will start on it.</summary>
/// <remarks>
/// The whole section is optional, so the defaults are part of the contract rather than a convenience: an operator who
/// writes nothing is answering under these numbers and has to be able to read them somewhere.
/// </remarks>
public sealed class MailAnsweringOptionsTests
{
    /// <summary>An absent section is a deployment answering under conservative ceilings, never one answering without any.</summary>
    [Fact]
    public void Defaults_ASectionNobodyWrote_AreTheConservativeCeilings()
    {
        // Act
        var settings = new MailAnsweringOptions();

        // Assert
        Assert.Equal(20, settings.MaxPassagesPerRetrieval);
        Assert.Equal(1_200, settings.MaxCharactersPerPassage);
        Assert.Equal(20_000, settings.MaxRetrievedCharactersPerRun);
        Assert.Equal(8, settings.MaxProviderCallsPerRun);
        Assert.Equal(80_000L, settings.MaxTokensPerRun);
        Assert.Equal(20_000, settings.MaxAnswerCharacters);
        Assert.Equal(20, settings.MaxCitations);
        Assert.Equal(TimeSpan.FromHours(1), settings.AggregatePeriod);
        Assert.Equal(30, settings.MaxRunsPerPeriod);
        Assert.Equal(300_000L, settings.MaxTokensPerPeriod);
        Assert.Empty(Validate(settings));
    }

    /// <summary>
    /// The one pair a reader can set into contradiction without either value being wrong on its own: every lookup would
    /// drop every passage, and the instance would answer from nothing while appearing to have read the mailbox.
    /// </summary>
    [Fact]
    public void Validate_ARunAllowedFewerCharactersThanOnePassageCarries_IsRefused()
    {
        // Arrange
        var settings = new MailAnsweringOptions
        {
            MaxCharactersPerPassage = 1_200,
            MaxRetrievedCharactersPerRun = 1_199,
        };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("MaxRetrievedCharactersPerRun", StringComparison.Ordinal));
    }

    /// <summary>Exactly one passage's worth is a working deployment: narrow, and stated rather than a mistake.</summary>
    [Fact]
    public void Validate_ARunAllowedExactlyOnePassage_IsAccepted()
    {
        // Arrange
        var settings = new MailAnsweringOptions
        {
            MaxCharactersPerPassage = 1_200,
            MaxRetrievedCharactersPerRun = 1_200,
        };

        // Act, Assert
        Assert.Empty(Validate(settings));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_APeriodThatCouldNeverElapse_IsRefused(int seconds)
    {
        // Arrange
        var settings = new MailAnsweringOptions { AggregatePeriod = TimeSpan.FromSeconds(seconds) };

        // Act
        var errors = Validate(settings);

        // Assert
        Assert.Contains(errors, error => error.Contains("AggregatePeriod", StringComparison.Ordinal));
    }

    /// <summary>
    /// The composition root maps this section before the container exists, so the attributes have to be reachable
    /// without the options pipeline: a value only <c>[Range]</c> refuses would otherwise first be noticed by a
    /// <c>Create</c> method and reach an operator as a framework stack trace.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AValueOnlyAnAttributeRefuses_IsReportedWithoutTheOptionsPipeline()
    {
        // Arrange
        var settings = new MailAnsweringOptions { MaxProviderCallsPerRun = 0 };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Contains(errors, error => error.Contains(nameof(settings.MaxProviderCallsPerRun), StringComparison.Ordinal));
    }

    /// <summary>The cross-field rule lives in Validate, and the same call has to reach it or half the rules would be skipped.</summary>
    [Fact]
    public void FindConfigurationErrors_AContradictionOnlyValidateFinds_IsReportedByTheSameCall()
    {
        // Arrange
        var settings = new MailAnsweringOptions
        {
            MaxCharactersPerPassage = 1_200,
            MaxRetrievedCharactersPerRun = 1_199,
        };

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Contains(errors, error => error.Contains(nameof(settings.MaxRetrievedCharactersPerRun), StringComparison.Ordinal));
    }

    /// <summary>Every default has to survive the same call, or an instance that configured nothing would refuse to start.</summary>
    [Fact]
    public void FindConfigurationErrors_TheDefaults_ReportNothing()
    {
        // Act, Assert
        Assert.Empty(new MailAnsweringOptions().FindConfigurationErrors());
    }

    private static IReadOnlyList<string> Validate(MailAnsweringOptions settings) =>
    [
        .. settings
            .Validate(new ValidationContext(settings))
            .Select(result => result.ErrorMessage ?? string.Empty),
    ];
}

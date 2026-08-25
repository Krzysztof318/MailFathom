// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers what a deployment may declare about freeing the database copies the move left beside its objects.</summary>
/// <remarks>
/// Both settings bound an act that cannot be undone, so a declaration that would make one meaningless — a hold measured
/// backwards, a batch of nothing or of an unbounded number of rows — is refused while the deployment starts rather than
/// met by an operator asking for a release that frees nothing or never answers.
/// </remarks>
public sealed class ContentReleaseOptionsTests
{
    /// <summary>The defaults are what a deployment that says nothing about the release runs, so they have to be usable.</summary>
    [Fact]
    public void FindConfigurationErrors_ADeploymentThatDeclaresNothing_IsAccepted()
    {
        // Arrange
        var options = new ContentReleaseOptions();

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A hold measured backwards would free copies verified in the future, which is nobody's intention.</summary>
    [Fact]
    public void FindConfigurationErrors_ANegativeSafetyInterval_IsRefusedNamingTheKey()
    {
        // Arrange
        var options = new ContentReleaseOptions { SafetyInterval = TimeSpan.FromHours(-1) };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentReleaseOptions.SectionPath}:{nameof(ContentReleaseOptions.SafetyInterval)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A hold nobody could have meant is refused while the host reads it rather than when an operator asks to free
    /// something: an interval this wide puts the batch's cutoff before any instant a timestamp can carry.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ASafetyIntervalPastTheCeiling_IsRefusedNamingTheKey()
    {
        // Arrange
        var options = new ContentReleaseOptions
        {
            SafetyInterval = ContentReleaseOptions.MaximumSafetyInterval + TimeSpan.FromDays(1),
        };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentReleaseOptions.SectionPath}:{nameof(ContentReleaseOptions.SafetyInterval)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>Zero is the default rather than a mistake: it says the hold is the operator's own decision and nothing else.</summary>
    [Fact]
    public void FindConfigurationErrors_ASafetyIntervalOfNothing_IsAccepted()
    {
        // Arrange
        var options = new ContentReleaseOptions { SafetyInterval = TimeSpan.Zero };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A batch of nothing never finishes, and one past the ceiling stops being the interruptible step this is.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ContentReleaseOptions.MaximumPayloadsPerBatch + 1)]
    public void FindConfigurationErrors_ABatchOutsideThePermittedRange_IsRefusedNamingTheKey(int payloadsPerBatch)
    {
        // Arrange
        var options = new ContentReleaseOptions { PayloadsPerBatch = payloadsPerBatch };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentReleaseOptions.SectionPath}:{nameof(ContentReleaseOptions.PayloadsPerBatch)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>The two ends of the range are declarations somebody meant, so neither is refused.</summary>
    [Fact]
    public void FindConfigurationErrors_ABatchAtEitherEndOfTheRange_IsAccepted()
    {
        // Arrange
        var smallest = new ContentReleaseOptions { PayloadsPerBatch = 1 };
        var largest = new ContentReleaseOptions
        {
            PayloadsPerBatch = ContentReleaseOptions.MaximumPayloadsPerBatch,
        };

        // Act
        var errors = smallest.FindConfigurationErrors().Concat(largest.FindConfigurationErrors());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Every faulty setting is reported, because an operator repairing one at a time repairs one per restart.</summary>
    [Fact]
    public void FindConfigurationErrors_ABlockWrongInEveryWay_ReportsEachSettingSeparately()
    {
        // Arrange
        var options = new ContentReleaseOptions
        {
            SafetyInterval = TimeSpan.FromSeconds(-1),
            PayloadsPerBatch = 0,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(2, errors.Count());
    }

    /// <summary>What a release is performed under is read from the declaration rather than restated beside it.</summary>
    [Fact]
    public void ToReleaseOptions_ADeclaredBlock_CarriesTheHoldAndTheBound()
    {
        // Arrange
        var options = new ContentReleaseOptions
        {
            SafetyInterval = TimeSpan.FromDays(7),
            PayloadsPerBatch = 50,
        };

        // Act
        var bounds = options.ToReleaseOptions();

        // Assert
        Assert.Equal((TimeSpan.FromDays(7), 50), (bounds.SafetyInterval, bounds.PayloadsPerBatch));
    }

    /// <summary>A faulty bound stops a deployment that could act on it, which is any deployment: the route is always served.</summary>
    [Fact]
    public void Validate_TheObjectStorageBackendWithAFaultyReleaseBlock_FailsStartupNamingTheKey()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.ObjectStorage,
            ObjectStorage = ContentStorageOptionsTests.UsableEndpoint(),
            Release = new ContentReleaseOptions { PayloadsPerBatch = 0 },
        };

        // Act
        var results = Validate(options);

        // Assert
        var failure = Assert.Single(results);
        Assert.Equal([nameof(ContentStorageOptions.Release)], failure.MemberNames);
        Assert.Contains(
            $"{ContentReleaseOptions.SectionPath}:{nameof(ContentReleaseOptions.PayloadsPerBatch)}",
            failure.ErrorMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment holding its content in the database is judged on this block too, unlike the move's: what a release
    /// frees are copies an earlier move left, and a deployment that carried its mail and then selected the database
    /// again for new writes still holds them.
    /// </summary>
    [Fact]
    public void Validate_TheDatabaseBackendWithAFaultyReleaseBlock_IsRefusedJustTheSame()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.Database,
            Release = new ContentReleaseOptions { PayloadsPerBatch = 0 },
        };

        // Act
        var results = Validate(options);

        // Assert
        var failure = Assert.Single(results);
        Assert.Equal([nameof(ContentStorageOptions.Release)], failure.MemberNames);
    }

    private static ValidationResult[] Validate(ContentStorageOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}

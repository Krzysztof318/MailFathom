// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers what a deployment may declare about the rate its already-stored content is carried at.</summary>
/// <remarks>
/// Each of the three bounds a move's cost while it runs, and each has a value that would leave the move running forever
/// without moving anything — a pass of no payloads, a ceiling of no bytes, an interval nothing elapses. A deployment
/// that declared one must be refused while it starts rather than discovered by an operator watching a figure that never
/// advances.
/// </remarks>
public sealed class ContentMoveOptionsTests
{
    /// <summary>The defaults are what a deployment that says nothing about the move runs, so they have to be usable.</summary>
    [Fact]
    public void FindConfigurationErrors_ADeploymentThatDeclaresNothing_IsAccepted()
    {
        // Arrange
        var options = new ContentMoveOptions();

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Below a second the move stops being background work, and beyond an hour it would not finish in a year.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(3_601)]
    public void FindConfigurationErrors_AnIntervalOutsideThePermittedRange_IsRefusedNamingTheKey(double seconds)
    {
        // Arrange
        var options = new ContentMoveOptions { Interval = TimeSpan.FromSeconds(seconds) };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentMoveOptions.SectionPath}:{nameof(ContentMoveOptions.Interval)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>The two ends of the range are declarations somebody meant, so neither is refused.</summary>
    [Fact]
    public void FindConfigurationErrors_AnIntervalAtEitherEndOfTheRange_IsAccepted()
    {
        // Arrange
        var shortest = new ContentMoveOptions { Interval = ContentMoveOptions.MinimumInterval };
        var longest = new ContentMoveOptions { Interval = ContentMoveOptions.MaximumInterval };

        // Act
        var errors = shortest.FindConfigurationErrors().Concat(longest.FindConfigurationErrors());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A pass that carries no payload is a move that runs forever and moves nothing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindConfigurationErrors_APassCarryingNoPayload_IsRefusedNamingTheKey(int payloadsPerPass)
    {
        // Arrange
        var options = new ContentMoveOptions { PayloadsPerPass = payloadsPerPass };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentMoveOptions.SectionPath}:{nameof(ContentMoveOptions.PayloadsPerPass)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>A pass ends on whichever ceiling it reaches first, so a ceiling of nothing ends it before its first payload.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindConfigurationErrors_AVolumeCeilingOfNothing_IsRefusedNamingTheKey(long maxBytesPerPass)
    {
        // Arrange
        var options = new ContentMoveOptions { MaxBytesPerPass = maxBytesPerPass };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.Contains(
            $"{ContentMoveOptions.SectionPath}:{nameof(ContentMoveOptions.MaxBytesPerPass)}",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>Every faulty setting is reported, because an operator repairing one at a time repairs one per restart.</summary>
    [Fact]
    public void FindConfigurationErrors_ABlockWrongInEveryWay_ReportsEachSettingSeparately()
    {
        // Arrange
        var options = new ContentMoveOptions
        {
            Interval = TimeSpan.Zero,
            PayloadsPerPass = 0,
            MaxBytesPerPass = 0,
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(3, errors.Count());
    }

    /// <summary>What the pass is bounded by is read from the declaration rather than restated beside it.</summary>
    [Fact]
    public void ToMoveOptions_ADeclaredBlock_CarriesTheTwoBoundsAPassEndsOn()
    {
        // Arrange
        var options = new ContentMoveOptions { PayloadsPerPass = 7, MaxBytesPerPass = 1_024 };

        // Act
        var bounds = options.ToMoveOptions();

        // Assert
        Assert.Equal((7, 1_024L), (bounds.PayloadsPerPass, bounds.MaxBytesPerPass));
    }

    /// <summary>The block is part of the section it sits in, so a faulty bound stops a deployment that could act on it.</summary>
    [Fact]
    public void Validate_TheObjectStorageBackendWithAFaultyMoveBlock_FailsStartupNamingTheKey()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.ObjectStorage,
            ObjectStorage = ContentStorageOptionsTests.UsableEndpoint(),
            Move = new ContentMoveOptions { PayloadsPerPass = 0 },
        };

        // Act
        var results = Validate(options);

        // Assert
        var failure = Assert.Single(results);
        Assert.Equal([nameof(ContentStorageOptions.Move)], failure.MemberNames);
        Assert.Contains(
            $"{ContentMoveOptions.SectionPath}:{nameof(ContentMoveOptions.PayloadsPerPass)}",
            failure.ErrorMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment storing content in the database has nowhere to move it to and never runs a pass, so a bound it
    /// declared for a backend it did not select must not be what stops it from starting.
    /// </summary>
    [Fact]
    public void Validate_TheDatabaseBackendWithAFaultyMoveBlock_JudgesItNotAtAll()
    {
        // Arrange
        var options = new ContentStorageOptions
        {
            Backend = ContentStorageBackend.Database,
            Move = new ContentMoveOptions { PayloadsPerPass = 0, MaxBytesPerPass = 0 },
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    private static ValidationResult[] Validate(ContentStorageOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return [.. results];
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Embeddings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Embeddings;

/// <summary>Covers what startup refuses about the backfill's pacing, and what an unconfigured deployment gets.</summary>
public sealed class EmbeddingBackfillOptionsTests
{
    /// <summary>
    /// A deployment that configures nothing still backfills, because an instance that has been synchronizing for months
    /// would otherwise activate a profile and find that semantic search covers only the mail that arrived afterwards.
    /// </summary>
    [Fact]
    public void Validate_NothingConfigured_BackfillsOnADefaultThatMakesProgressWithoutBeingUnbounded()
    {
        // Arrange
        var settings = new EmbeddingBackfillOptions();

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Empty(errors);
        Assert.True(settings.Enabled);
        Assert.True(settings.IdleSweepInterval > settings.Interval);
        Assert.True(settings.BatchSize * settings.MaxBatchesPerRun > 0);
    }

    /// <summary>
    /// The two bounds are what stands between a run and an unbounded provider bill, so a value that is not positive is
    /// refused rather than read as "no bound".
    /// </summary>
    [Theory]
    [InlineData(0, 5, nameof(EmbeddingBackfillOptions.BatchSize))]
    [InlineData(-1, 5, nameof(EmbeddingBackfillOptions.BatchSize))]
    [InlineData(501, 5, nameof(EmbeddingBackfillOptions.BatchSize))]
    [InlineData(20, 0, nameof(EmbeddingBackfillOptions.MaxBatchesPerRun))]
    [InlineData(20, -1, nameof(EmbeddingBackfillOptions.MaxBatchesPerRun))]
    public void Validate_ARunBoundOutsideItsRange_IsRefused(int batchSize, int maxBatchesPerRun, string expectedMember)
    {
        // Arrange
        var settings = new EmbeddingBackfillOptions
        {
            BatchSize = batchSize,
            MaxBatchesPerRun = maxBatchesPerRun,
        };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(expectedMember, StringComparer.Ordinal));
    }

    /// <summary>An interval of zero would spend against a provider as fast as the database can hand messages over.</summary>
    [Theory]
    [InlineData(0, 0, nameof(EmbeddingBackfillOptions.Interval))]
    [InlineData(-1, 0, nameof(EmbeddingBackfillOptions.Interval))]
    [InlineData(30, 0, nameof(EmbeddingBackfillOptions.IdleSweepInterval))]
    [InlineData(30, -1, nameof(EmbeddingBackfillOptions.IdleSweepInterval))]
    public void Validate_AnIntervalThatIsNotPositive_IsRefused(
        int intervalSeconds,
        int idleSweepIntervalSeconds,
        string expectedMember)
    {
        // Arrange
        var settings = new EmbeddingBackfillOptions
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds),
            IdleSweepInterval = TimeSpan.FromSeconds(idleSweepIntervalSeconds),
        };

        // Act
        var errors = ValidateEveryProperty(settings);

        // Assert
        Assert.Contains(errors, error => error.MemberNames.Contains(expectedMember, StringComparer.Ordinal));
    }

    /// <summary>Both keys one sweep stops at reach the bounds the walk takes.</summary>
    [Fact]
    public void ToBackfillOptions_ConfiguredSection_CarriesBothKeysTheSweepStopsAt()
    {
        // Arrange
        var settings = new EmbeddingBackfillOptions { BatchSize = 9, MaxBatchesPerRun = 4 };

        // Act
        var bounds = settings.ToBackfillOptions();

        // Assert
        Assert.Equal(9, bounds.BatchSize);
        Assert.Equal(4, bounds.MaxBatchesPerRun);
    }

    /// <summary>Runs the attribute rules, which is what the options framework does with this type on start.</summary>
    private static List<ValidationResult> ValidateEveryProperty(EmbeddingBackfillOptions settings)
    {
        List<ValidationResult> errors = [];
        Validator.TryValidateObject(settings, new ValidationContext(settings), errors, validateAllProperties: true);

        return errors;
    }
}

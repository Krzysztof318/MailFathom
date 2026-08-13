// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Jobs;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Jobs;

public sealed class JobQueueOptionsTests
{
    /// <summary>
    /// A job allowed to run for as long as its lease is held can be claimed by a second worker while the first is still
    /// running it, which is the one guarantee no attribute on a single property can express.
    /// </summary>
    [Theory]
    [InlineData(300, 300)]
    [InlineData(600, 300)]
    public void Validate_ATimeoutThatIsNotShorterThanTheLease_IsRefused(int timeoutSeconds, int leaseSeconds)
    {
        // Arrange
        var settings = new JobQueueOptions
        {
            LeaseDuration = TimeSpan.FromSeconds(leaseSeconds),
            ExecutionTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage?.Contains("Jobs:ExecutionTimeout", StringComparison.Ordinal) is true);
    }

    /// <summary>
    /// A ceiling below the delay the growth starts from caps every retry at the ceiling and leaves the backoff with
    /// nothing to grow, which is the second rule no attribute on a single property can express.
    /// </summary>
    [Theory]
    [InlineData(300, 60)]
    [InlineData(300, 299)]
    public void Validate_ARetryCeilingBelowTheDelayItGrowsFrom_IsRefused(int baseDelaySeconds, int maxDelaySeconds)
    {
        // Arrange
        var settings = new JobQueueOptions
        {
            RetryBaseDelay = TimeSpan.FromSeconds(baseDelaySeconds),
            RetryMaxDelay = TimeSpan.FromSeconds(maxDelaySeconds),
        };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage?.Contains("Jobs:RetryMaxDelay", StringComparison.Ordinal) is true
                && result.MemberNames.Contains(nameof(JobQueueOptions.RetryMaxDelay), StringComparer.Ordinal));
    }

    /// <summary>The two ceilings are equal at the boundary, where the growth has nowhere left to go but is not inverted.</summary>
    [Fact]
    public void Validate_ARetryCeilingEqualToTheDelayItGrowsFrom_IsAccepted()
    {
        // Arrange
        var settings = new JobQueueOptions
        {
            RetryBaseDelay = TimeSpan.FromSeconds(300),
            RetryMaxDelay = TimeSpan.FromSeconds(300),
        };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>A small instance runs the defaults unchanged, so they have to satisfy the rule they exist under.</summary>
    [Fact]
    public void Validate_TheDefaults_AreAccepted()
    {
        // Act
        var results = Validate(new JobQueueOptions());

        // Assert
        Assert.Empty(results);
    }

    /// <summary>Every bound is a pacing control, and a nonsensical one is refused at startup rather than at the first pass.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_ABatchSizeOutsideItsRange_IsRefused(int batchSize)
    {
        // Arrange
        var settings = new JobQueueOptions { BatchSize = batchSize };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(JobQueueOptions.BatchSize), StringComparer.Ordinal));
    }

    /// <summary>
    /// A per-type ceiling above the ceiling for the whole instance can never be reached, so it states a bound nobody
    /// has — the third rule no attribute on a single property can express.
    /// </summary>
    [Theory]
    [InlineData(2, 3)]
    [InlineData(1, 32)]
    public void Validate_APerTypeCeilingAboveTheProcessCeiling_IsRefused(
        int maxConcurrentJobs,
        int maxConcurrentJobsPerType)
    {
        // Arrange
        var settings = new JobQueueOptions
        {
            MaxConcurrentJobs = maxConcurrentJobs,
            MaxConcurrentJobsPerType = maxConcurrentJobsPerType,
        };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage?.Contains("Jobs:MaxConcurrentJobsPerType", StringComparison.Ordinal) is true
                && result.MemberNames.Contains(
                    nameof(JobQueueOptions.MaxConcurrentJobsPerType),
                    StringComparer.Ordinal));
    }

    /// <summary>The two ceilings are equal at the boundary, which is one type being allowed the whole instance.</summary>
    [Fact]
    public void Validate_APerTypeCeilingEqualToTheProcessCeiling_IsAccepted()
    {
        // Arrange
        var settings = new JobQueueOptions { MaxConcurrentJobs = 3, MaxConcurrentJobsPerType = 3 };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>A ceiling outside its range is refused for the same reason a batch size is: it is read at startup or never.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Validate_AProcessCeilingOutsideItsRange_IsRefused(int maxConcurrentJobs)
    {
        // Arrange
        var settings = new JobQueueOptions
        {
            MaxConcurrentJobs = maxConcurrentJobs,
            MaxConcurrentJobsPerType = 1,
        };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(JobQueueOptions.MaxConcurrentJobs), StringComparer.Ordinal));
    }

    /// <summary>A queue depth of nothing would refuse every enqueue, and one without a ceiling would bound nothing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1000001)]
    public void Validate_AQueueDepthOutsideItsRange_IsRefused(int maxQueueDepthPerType)
    {
        // Arrange
        var settings = new JobQueueOptions { MaxQueueDepthPerType = maxQueueDepthPerType };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(JobQueueOptions.MaxQueueDepthPerType),
                StringComparer.Ordinal));
    }

    private static List<ValidationResult> Validate(JobQueueOptions settings)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        return results;
    }
}

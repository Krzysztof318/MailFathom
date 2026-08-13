// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration.Jobs;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Jobs;

public sealed class JobWorkerOptionsTests
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
        var settings = new JobWorkerOptions
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

    /// <summary>A small instance runs the defaults unchanged, so they have to satisfy the rule they exist under.</summary>
    [Fact]
    public void Validate_TheDefaults_AreAccepted()
    {
        // Act
        var results = Validate(new JobWorkerOptions());

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
        var settings = new JobWorkerOptions { BatchSize = batchSize };

        // Act
        var results = Validate(settings);

        // Assert
        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(JobWorkerOptions.BatchSize), StringComparer.Ordinal));
    }

    private static List<ValidationResult> Validate(JobWorkerOptions settings)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);

        return results;
    }
}

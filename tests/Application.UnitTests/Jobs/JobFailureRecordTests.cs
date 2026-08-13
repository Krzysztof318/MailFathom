// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobFailureRecordTests
{
    /// <summary>The record is what an operator reads off a stopped job, so both halves of it survive the write.</summary>
    [Fact]
    public void Create_AClassifiedFailure_KeepsTheVerdictAndTheReason()
    {
        // Act
        var record = JobFailureRecord.Create(JobFailureClassification.Transient, "SocketException");

        // Assert
        Assert.Equal(JobFailureClassification.Transient, record.Classification);
        Assert.Equal("SocketException", record.Reason);
    }

    /// <summary>A reason nobody can read names nothing, and the column it is written to would carry whitespace instead.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankReason_IsRefused(string reason)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => JobFailureRecord.Create(JobFailureClassification.Permanent, reason));
    }

    /// <summary>A verdict outside the two the queue acts on would decide nothing, so it is refused where it is composed.</summary>
    [Fact]
    public void Create_AClassificationOutsideTheSet_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JobFailureRecord.Create((JobFailureClassification)7, "SocketException"));
    }

    /// <summary>
    /// Failing the one write whose purpose is to record a failure would leave the job held with nothing said about why,
    /// so a reason past the column's bound is shortened rather than refused.
    /// </summary>
    [Fact]
    public void Create_AReasonLongerThanTheColumnAllows_IsShortenedRatherThanRefused()
    {
        // Arrange
        var overlongReason = new string('r', JobFailureRecord.MaximumReasonLength + 20);

        // Act
        var record = JobFailureRecord.Create(JobFailureClassification.Permanent, overlongReason);

        // Assert
        Assert.Equal(JobFailureRecord.MaximumReasonLength, record.Reason.Length);
    }

    /// <summary>
    /// A job claimed for a type nothing runs cannot be helped by another attempt, and one that ran out of time can, so
    /// the two well-known records disagree deliberately.
    /// </summary>
    [Fact]
    public void WellKnownRecords_TheTwoFailuresNothingRaised_AreClassifiedApart()
    {
        // Assert
        Assert.Equal(JobFailureClassification.Permanent, JobFailureRecord.HandlerMissing.Classification);
        Assert.Equal(JobFailureClassification.Transient, JobFailureRecord.ExecutionTimedOut.Classification);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Jobs;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

/// <summary>Covers what a stopped row means to the operator reading it.</summary>
public sealed class DeadLetteredJobMappingTests
{
    private static readonly DateTimeOffset EnqueuedAt = new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset StoppedAt = new(2026, 8, 13, 9, 30, 0, TimeSpan.Zero);

    /// <summary>What the row keeps is what an operator acts on: the identity, the attempts spent, and why it stopped.</summary>
    [Fact]
    public void ToDeadLetteredJob_ARowThisBuildDeclares_RebuildsTheIdentityTheAttemptsAndTheFailure()
    {
        // Arrange
        var row = Row(JobType.ClassifyEmailSpam.Name);

        // Act
        var job = DeadLetteredJobMapping.ToDeadLetteredJob(row);

        // Assert
        Assert.Equal(JobType.ClassifyEmailSpam, job?.JobType);
        Assert.Equal("account:work|email:1", job?.Key.Value);
        Assert.Equal("work", job?.AccountId?.Value);
        Assert.Equal(5, job?.AttemptCount);
        Assert.Equal(JobFailureClassification.Permanent, job?.LastFailure?.Classification);
        Assert.Equal("PayloadUnreadable", job?.LastFailure?.Reason);
        Assert.Equal(StoppedAt, job?.DeadLetteredAt);
    }

    /// <summary>
    /// A rolling deployment leaves rows written by a build that declared more types than this one. Reporting one under
    /// a name nothing runs would offer an operator a retry no worker could ever claim, so it is left out entirely.
    /// </summary>
    [Fact]
    public void ToDeadLetteredJob_ARowNamingATypeThisBuildDoesNotDeclare_IsLeftOut()
    {
        // Arrange
        var row = Row("a-type-from-a-later-build");

        // Act
        var job = DeadLetteredJobMapping.ToDeadLetteredJob(row);

        // Assert
        Assert.Null(job);
    }

    /// <summary>
    /// The two failure columns are nullable, so a row that reached this state without one is reported as a job with no
    /// recorded reason rather than refused on the way to an operator who is looking at it because something went wrong.
    /// </summary>
    [Fact]
    public void ToDeadLetteredJob_ARowRecordingNoFailure_IsReportedWithoutOne()
    {
        // Arrange
        var row = Row(JobType.ClassifyEmailSpam.Name) with
        {
            LastFailureClassification = null,
            LastFailureReason = null,
        };

        // Act
        var job = DeadLetteredJobMapping.ToDeadLetteredJob(row);

        // Assert
        Assert.NotNull(job);
        Assert.Null(job.LastFailure);
    }

    /// <summary>Work belonging to no account is ordinary, so the absence is carried rather than invented around.</summary>
    [Fact]
    public void ToDeadLetteredJob_ARowBelongingToNoAccount_CarriesNoAccount()
    {
        // Arrange
        var row = Row(JobType.ClassifyEmailSpam.Name) with { MailboxAccountId = null };

        // Act
        var job = DeadLetteredJobMapping.ToDeadLetteredJob(row);

        // Assert
        Assert.Null(job?.AccountId);
    }

    private static DeadLetteredJobRow Row(string jobTypeName) => new(
        Guid.CreateVersion7(),
        jobTypeName,
        "account:work|email:1",
        "work",
        5,
        JobFailureClassification.Permanent,
        "PayloadUnreadable",
        EnqueuedAt,
        StoppedAt);
}

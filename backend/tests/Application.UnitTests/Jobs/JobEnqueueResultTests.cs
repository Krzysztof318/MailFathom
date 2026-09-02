// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs;

public sealed class JobEnqueueResultTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A written job and a job that was already there both name the row, which is what a caller refers to it by.</summary>
    [Fact]
    public void CreatedAndAlreadyEnqueued_AJobTheQueueHolds_NameIt()
    {
        // Arrange
        var jobId = JobId.Create(Guid.CreateVersion7(Noon));

        // Act
        var created = JobEnqueueResult.Created(jobId);
        var alreadyEnqueued = JobEnqueueResult.AlreadyEnqueued(jobId);

        // Assert
        Assert.Equal(JobEnqueueOutcome.Created, created.Outcome);
        Assert.Equal(JobEnqueueOutcome.AlreadyEnqueued, alreadyEnqueued.Outcome);
        Assert.Equal(jobId, created.JobId);
        Assert.Equal(jobId, alreadyEnqueued.JobId);
    }

    /// <summary>
    /// A refusal wrote nothing and found nothing, so there is no row for it to point at. The outcome is what a caller
    /// reads, and the absent identifier is what stops it acting as though work had been queued.
    /// </summary>
    [Fact]
    public void RefusedAtCapacity_AQueueThatIsFull_NamesNoJob()
    {
        // Act
        var refused = JobEnqueueResult.RefusedAtCapacity();

        // Assert
        Assert.Equal(JobEnqueueOutcome.RefusedAtCapacity, refused.Outcome);
        Assert.Null(refused.JobId);
    }
}

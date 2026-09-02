// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

public sealed class JobQueueDepthQueryTests
{
    private const string JobTypeName = "classify-email-spam";
    private const int DepthBound = 5;

    /// <summary>
    /// The bound is a comparison rather than a total, so the query stops at it. Reading the whole queue instead would
    /// make every enqueue pay for the size of a backlog it only needs to compare against, and nothing about the answer
    /// would change.
    /// </summary>
    [Fact]
    public void Compose_ADepthBound_StopsAtItRatherThanCountingTheWholeQueue()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = JobQueueDepthQuery.Compose(context.Jobs.AsNoTracking(), JobTypeName, DepthBound).ToQueryString();

        // Assert
        Assert.Contains("LIMIT", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only what is waiting counts. A job a worker holds is running and is bounded by the concurrency ceiling instead,
    /// so a predicate that admitted it would report an instance draining its queue as one filling it.
    /// </summary>
    [Fact]
    public void Compose_AJobType_CountsOnlyThePendingJobsOfThatType()
    {
        // Arrange
        using var context = DesignTimeContext();

        // Act
        var sql = JobQueueDepthQuery.Compose(context.Jobs.AsNoTracking(), JobTypeName, DepthBound).ToQueryString();

        // Assert
        Assert.Contains(nameof(JobState.Pending), sql, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JobState.Claimed), sql, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JobState.DeadLettered), sql, StringComparison.Ordinal);
        Assert.Contains("\"JobType\"", sql, StringComparison.Ordinal);
    }

    private static MailFathomDbContext DesignTimeContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}

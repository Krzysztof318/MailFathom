// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Jobs;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

public sealed class JobRecordMappingTests
{
    private static readonly DateTimeOffset ClaimedAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToLeasedJob_AClaimedRow_RebuildsTheJobWithItsLeaseAndAttempt()
    {
        // Arrange
        var entity = ClaimedRow();

        // Act
        var job = JobRecordMapping.ToLeasedJob(entity);

        // Assert
        Assert.Equal(JobType.ClassifyEmailSpam, job.JobType);
        Assert.Equal(JobId.Create(entity.Id), job.JobId);
        Assert.Equal(JobIdempotencyKey.Create("account-a/INBOX#1/12345/4711"), job.Key);
        Assert.Equal(MailAccountId.Create("account-a"), job.AccountId);
        Assert.Equal(3, job.AttemptCount);
        Assert.Equal(JobLeaseOwner.Create("attempt-a"), job.Lease.Owner);
        Assert.Equal(ClaimedAt.AddMinutes(5), job.Lease.ExpiresAt);
    }

    /// <summary>The row is what carries the enqueuing trace across the queue, so a claim reads it back onto the job.</summary>
    [Fact]
    public void ToLeasedJob_ARowRecordingTheEnqueuingTrace_RebuildsItOntoTheJob()
    {
        // Arrange
        var entity = ClaimedRow();
        entity.EnqueuedTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01";
        entity.EnqueuedTraceState = "vendor=state";

        // Act
        var job = JobRecordMapping.ToLeasedJob(entity);

        // Assert
        Assert.NotNull(job.EnqueuedTrace);
        Assert.Equal("00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01", job.EnqueuedTrace.TraceParent);
        Assert.Equal("vendor=state", job.EnqueuedTrace.TraceState);
    }

    /// <summary>Every row written before the column existed reads this way, and the attempt at it links to nothing.</summary>
    [Fact]
    public void ToLeasedJob_ARowRecordingNoTrace_ReportsNoneRatherThanFailing()
    {
        // Arrange
        var entity = ClaimedRow();

        // Act
        var job = JobRecordMapping.ToLeasedJob(entity);

        // Assert
        Assert.Null(job.EnqueuedTrace);
    }

    /// <summary>A job belonging to no account is an ordinary case, and its column is null rather than a placeholder.</summary>
    [Fact]
    public void ToLeasedJob_ARowBelongingToNoAccount_ReportsNoAccountRatherThanOne()
    {
        // Arrange
        var entity = ClaimedRow();
        entity.MailboxAccountId = null;

        // Act
        var job = JobRecordMapping.ToLeasedJob(entity);

        // Assert
        Assert.Null(job.AccountId);
    }

    /// <summary>
    /// A type this build does not declare is what an older replica meets when a newer one introduces one. The claim
    /// leaves such a row alone, so reaching this mapping with one means the filter stopped working — which is a defect
    /// rather than a row to run.
    /// </summary>
    [Fact]
    public void ToLeasedJob_ARowNamingATypeThisBuildDoesNotDeclare_IsRefused()
    {
        // Arrange
        var entity = ClaimedRow();
        entity.JobType = "a-type-a-later-build-declares";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => JobRecordMapping.ToLeasedJob(entity));
    }

    /// <summary>
    /// Answering with a job nobody holds would let work run outside the exclusion the lease is, so a row read as
    /// claimed and carrying no lease stops the read instead.
    /// </summary>
    [Fact]
    public void ToLeasedJob_ARowCarryingNoLease_IsRefused()
    {
        // Arrange
        var entity = ClaimedRow();
        entity.LeaseOwner = null;
        entity.LeaseExpiresAt = null;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => JobRecordMapping.ToLeasedJob(entity));
    }

    private static JobEntity ClaimedRow() => new()
    {
        Id = Guid.CreateVersion7(ClaimedAt),
        JobType = JobType.ClassifyEmailSpam.Name,
        IdempotencyKey = "account-a/INBOX#1/12345/4711",
        Payload = """
                  {"ownerId":"11111111-1111-1111-1111-111111111111","accountId":"account-a",
                   "folderAlias":"INBOX","folderResolutionGeneration":1,"uidValidity":12345,"uid":4711}
                  """,
        MailboxAccountId = "account-a",
        State = JobState.Claimed,
        AvailableAt = ClaimedAt,
        EnqueuedAt = ClaimedAt,
        StateChangedAt = ClaimedAt,
        AttemptCount = 3,
        LeaseOwner = "attempt-a",
        LeaseExpiresAt = ClaimedAt.AddMinutes(5),
    };
}

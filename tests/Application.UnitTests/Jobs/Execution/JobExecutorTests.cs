// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobExecutorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(9);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider timeProvider = new(Noon);
    private readonly IJobStore store = Substitute.For<IJobStore>();

    /// <summary>Dispatch is what makes the worker a mechanism rather than a feature: the payload reaches the handler its type names.</summary>
    [Fact]
    public async Task ExecuteAsync_AJobWhoseTypeHasAHandler_RunsItAndRecordsTheJobAsDone()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.CompleteAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor(handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.Succeeded, result.Outcome);
        Assert.Same(job.Payload, handler.ReceivedPayload);
        await this.store.Received(1).CompleteAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A job nothing can run must stop rather than be handed out again forever, so it is recorded as failed instead of
    /// left for its lease to expire.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AJobWhoseTypeHasNoHandler_RecordsItAsFailedRatherThanLeavingItClaimable()
    {
        // Arrange
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor();

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.HandlerMissing, result.Outcome);
        await this.store.Received(1).FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ReleaseAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The exception travels with the result, because only the caller knows at what level a failure is reported.</summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerThatRaises_RecordsTheFailureAndCarriesTheException()
    {
        // Arrange
        var raised = new InvalidOperationException("the handler could not finish");
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, (_, _) => Task.FromException(raised));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor(handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.HandlerFailed, result.Outcome);
        Assert.Same(raised, result.Failure);
        await this.store.Received(1).FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A stuck handler is stopped through its token rather than abandoned while it runs, which is what keeps one slow
    /// job from taking a worker out of service.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerThatOutlastsTheTimeout_CancelsItAndRecordsTheJobAsFailed()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            RecordingJobHandler.BlockUntilCancelled(started));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);
        this.AllowLeaseRenewal(job);

        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, CancellationToken.None);
        await started.Task;
        this.timeProvider.Advance(ExecutionTimeout);
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.TimedOut, result.Outcome);
        await this.store.Received(1).FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
    }

    /// <summary>A job that legitimately takes longer than one lease must not have its work reclaimed underneath it.</summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerStillWorkingAtTheRenewalInterval_PushesTheLeaseFurtherOut()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var renewed = new TaskCompletionSource();
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            RecordingJobHandler.BlockUntilCancelled(started));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);

        this.store
            .RenewLeaseAsync(job.JobId, job.Lease.Owner, LeaseDuration, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                renewed.TrySetResult();

                return Task.FromResult<JobLease?>(new JobLease(job.Lease.Owner, Noon + LeaseDuration + LeaseDuration));
            });
        this.store.FailAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, CancellationToken.None);
        await started.Task;
        this.timeProvider.Advance(RenewalInterval);
        await renewed.Task;
        this.timeProvider.Advance(ExecutionTimeout - RenewalInterval);
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.TimedOut, result.Outcome);
        await this.store
            .Received(1)
            .RenewLeaseAsync(job.JobId, job.Lease.Owner, LeaseDuration, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refused renewal means another attempt already holds the job, so this one stops working and writes nothing:
    /// anything it produced from here would be a second execution's result.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ARenewalTheStoreRefuses_StopsTheHandlerAndRecordsNothing()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            RecordingJobHandler.BlockUntilCancelled(started));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);

        this.store
            .RenewLeaseAsync(job.JobId, job.Lease.Owner, LeaseDuration, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<JobLease?>(null));

        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, CancellationToken.None);
        await started.Task;
        this.timeProvider.Advance(RenewalInterval);
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.LeaseLost, result.Outcome);
        await this.store.DidNotReceive().CompleteAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().FailAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ReleaseAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A deployment must not read as a burst of failures: the job goes back to the queue immediately and no attempt is
    /// recorded against it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHostThatStopsWhileTheHandlerWorks_ReleasesTheLeaseRatherThanFailingTheJob()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            RecordingJobHandler.BlockUntilCancelled(started));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        using var stoppingSource = new CancellationTokenSource();
        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, stoppingSource.Token);
        await started.Task;
        await stoppingSource.CancelAsync();
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.ReleasedForShutdown, result.Outcome);
        await this.store.Received(1).ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().FailAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A handler that turns its cancellation into an exception of its own must not have a deployment recorded against
    /// its job. Releasing work that really was failing only runs it again; failing work a shutdown interrupted is a
    /// terminal row nobody asked for.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerRaisingSomethingElseAsTheHostStops_StillReleasesTheLease()
    {
        // Arrange
        var started = new TaskCompletionSource();
        using var stoppingSource = new CancellationTokenSource();
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, async (_, cancellationToken) =>
        {
            started.TrySetResult();

            var blocked = new TaskCompletionSource();

            await using var registration = cancellationToken.Register(() => blocked.TrySetResult());

            await blocked.Task;

            throw new InvalidOperationException("the handler gave up when its token was cancelled");
        });
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, stoppingSource.Token);
        await started.Task;
        await stoppingSource.CancelAsync();
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.ReleasedForShutdown, result.Outcome);
        await this.store.DidNotReceive().FailAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The jobs behind the one that was running are handed back too, rather than waiting out a lease nobody holds. That
    /// is what a batch claimed just before a shutdown needs.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AJobDispatchedWhileTheHostIsAlreadyStopping_ReleasesItWithoutRunningTheHandler()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        using var stoppingSource = new CancellationTokenSource();
        await stoppingSource.CancelAsync();

        var executor = this.ExecutorFor(handler);

        // Act
        var result = await executor.ExecuteAsync(job, stoppingSource.Token);

        // Assert
        Assert.Equal(JobExecutionOutcome.ReleasedForShutdown, result.Outcome);
        Assert.Equal(0, handler.RunCount);
        await this.store.Received(1).ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Without the compare-and-set an attempt that lost its lease and finished late would overwrite the outcome of the
    /// attempt that replaced it, so a refused write is reported as the lost lease it is.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ACompletionTheStoreRefuses_ReportsTheLeaseAsLost()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store.CompleteAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(false);

        var executor = this.ExecutorFor(handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.LeaseLost, result.Outcome);
    }

    /// <summary>What a result names is the queue's own vocabulary, so a report of one can carry nothing from the message.</summary>
    [Fact]
    public async Task ExecuteAsync_AnyJob_ReportsTheJobsOwnIdentityAndAttempt()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var job = LeasedJobFor(JobType.ClassifyEmailSpam, attemptCount: 3);
        this.store.CompleteAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>()).Returns(true);

        var executor = this.ExecutorFor(handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(job.JobId, result.JobId);
        Assert.Equal(JobType.ClassifyEmailSpam, result.JobType);
        Assert.Equal(3, result.AttemptCount);
        Assert.Null(result.Failure);
    }

    private static LeasedJob LeasedJobFor(JobType jobType, int attemptCount = 1) => new(
        JobId.Create(Guid.CreateVersion7(Noon)),
        jobType,
        JobIdempotencyKey.Create("account-a/inbox/1/42"),
        new EmailOccurrenceJobPayload
        {
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = 42,
        },
        AccountId: null,
        attemptCount,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon + LeaseDuration));

    /// <summary>Lets the lease keep being renewed, which is what a healthy long execution sees.</summary>
    private void AllowLeaseRenewal(LeasedJob job) => this.store
        .RenewLeaseAsync(job.JobId, job.Lease.Owner, LeaseDuration, Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<JobLease?>(new JobLease(job.Lease.Owner, Noon + LeaseDuration + LeaseDuration)));

    private JobExecutor ExecutorFor(params IJobHandler[] handlers) => new(
        this.store,
        new JobHandlerRegistry(handlers),
        JobExecutionSettings.Create(batchSize: 5, LeaseDuration, ExecutionTimeout),
        this.timeProvider);
}

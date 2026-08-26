// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.TestSupport;
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
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromMinutes(30);
    private const int MaxAttempts = 5;

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
    /// A job nothing can run must stop rather than be handed out again forever, so it is dead-lettered on the attempt
    /// that found no handler instead of spending the attempt budget discovering the same thing.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AJobWhoseTypeHasNoHandler_DeadLettersItRatherThanLeavingItClaimable()
    {
        // Arrange
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.AllowDeadLettering(job);

        var executor = this.ExecutorFor();

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.HandlerMissing, result.Outcome);
        Assert.Equal(JobFailureDisposition.DeadLettered, result.AttemptFailure?.Disposition);
        Assert.Equal(JobFailureClassification.Permanent, result.AttemptFailure?.Record.Classification);
        await this.store.Received(1).DeadLetterAsync(
            job.JobId,
            job.Lease.Owner,
            Arg.Is<JobFailureRecord>(failure => failure!.Reason == JobFailureRecord.HandlerMissing.Reason),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ReleaseAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Repeating a permanent failure cannot change the answer, so the budget is never spent on one: the job is terminal
    /// on its first attempt with every attempt still available.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerRaisingAPermanentFailure_DeadLettersTheJobOnItsFirstAttempt()
    {
        // Arrange
        var raised = new InvalidOperationException("the handler could not finish");
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, (_, _) => Task.FromException(raised));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        var failureClassifier = new StubJobFailureClassifier(JobFailureClassification.Permanent, "PermanentFailure");
        this.AllowDeadLettering(job);

        var executor = this.ExecutorFor(failureClassifier, handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.HandlerFailed, result.Outcome);
        Assert.Equal(JobFailureDisposition.DeadLettered, result.AttemptFailure?.Disposition);
        Assert.Equal("PermanentFailure", result.AttemptFailure?.Record.Reason);
        Assert.Same(raised, failureClassifier.ClassifiedFailure);
        await this.store.DidNotReceive().ScheduleRetryAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A transient failure is the one kind worth attempting again, and the job goes back to the queue behind a delay
    /// rather than immediately: a job returned at once would be taken again as fast as the queue can hand it out.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerRaisingATransientFailureWithAttemptsLeft_SchedulesAnotherAttemptAfterADelay()
    {
        // Arrange
        var raised = new TimeoutException("the dependency did not answer");
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, (_, _) => Task.FromException(raised));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam, attemptCount: 1);
        this.AllowRetryScheduling(job);

        var executor = this.ExecutorFor(
            new StubJobFailureClassifier(JobFailureClassification.Transient, "TransientFailure"),
            handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.HandlerFailed, result.Outcome);
        Assert.Equal(JobFailureDisposition.RetryScheduled, result.AttemptFailure?.Disposition);
        Assert.InRange(
            result.AttemptFailure?.NextAttemptAt ?? Noon,
            Noon + (RetryBaseDelay / 2),
            Noon + RetryMaxDelay);
        await this.store.Received(1).ScheduleRetryAsync(
            job.JobId,
            job.Lease.Owner,
            Arg.Is<JobFailureRecord>(failure =>
                failure!.Classification == JobFailureClassification.Transient && failure.Reason == "TransientFailure"),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().DeadLetterAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The bound is what stops a dependency that stays broken from being approached forever: the attempt that reaches
    /// it is terminal even though the failure itself is worth repeating.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ATransientFailureOnTheLastAllowedAttempt_DeadLettersTheJob()
    {
        // Arrange
        var raised = new TimeoutException("the dependency did not answer");
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, (_, _) => Task.FromException(raised));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam, attemptCount: MaxAttempts);
        this.AllowDeadLettering(job);

        var executor = this.ExecutorFor(
            new StubJobFailureClassifier(JobFailureClassification.Transient, "TransientFailure"),
            handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobFailureDisposition.DeadLettered, result.AttemptFailure?.Disposition);
        Assert.Equal(JobFailureClassification.Transient, result.AttemptFailure?.Record.Classification);
        Assert.Null(result.AttemptFailure?.NextAttemptAt);
        await this.store.Received(1).DeadLetterAsync(
            job.JobId,
            job.Lease.Owner,
            Arg.Any<JobFailureRecord>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A stuck handler is stopped through its token rather than abandoned while it runs, which is what keeps one slow
    /// job from taking a worker out of service. A timeout says the work did not finish in time rather than that it
    /// cannot, so the job is attempted again.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerThatOutlastsTheTimeout_CancelsItAndSchedulesAnotherAttempt()
    {
        // Arrange
        var started = new TaskCompletionSource();
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            RecordingJobHandler.BlockUntilCancelled(started));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.AllowRetryScheduling(job);
        this.AllowLeaseRenewal(job);

        var executor = this.ExecutorFor(handler);

        // Act
        var execution = executor.ExecuteAsync(job, CancellationToken.None);
        await started.Task;
        this.timeProvider.Advance(ExecutionTimeout);
        var result = await execution;

        // Assert
        Assert.Equal(JobExecutionOutcome.TimedOut, result.Outcome);
        Assert.Equal(JobFailureDisposition.RetryScheduled, result.AttemptFailure?.Disposition);
        await this.store.Received(1).ScheduleRetryAsync(
            job.JobId,
            job.Lease.Owner,
            Arg.Is<JobFailureRecord>(failure => failure!.Reason == JobFailureRecord.ExecutionTimedOut.Reason),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
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
        this.AllowRetryScheduling(job);

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
        Assert.Null(result.AttemptFailure);
        await this.store.DidNotReceive().CompleteAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().DeadLetterAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ScheduleRetryAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ReleaseAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A deployment must not read as a burst of failures: the job goes back to the queue immediately and nothing is
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
        Assert.Null(result.AttemptFailure);
        await this.store.Received(1).ReleaseAsync(job.JobId, job.Lease.Owner, Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().DeadLetterAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().ScheduleRetryAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A handler that turns its cancellation into an exception of its own must not have a deployment recorded against
    /// its job. Releasing work that really was failing only runs it again; failing work a shutdown interrupted spends an
    /// attempt on the operator's act.
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
        await this.store.DidNotReceive().DeadLetterAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<JobFailureRecord>(),
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

    /// <summary>The same compare-and-set guards a scheduled retry, so a late attempt cannot reopen a job somebody else holds.</summary>
    [Fact]
    public async Task ExecuteAsync_ARetryTheStoreRefuses_ReportsTheLeaseAsLostAndRecordsNoFailure()
    {
        // Arrange
        var handler = new RecordingJobHandler(
            JobType.ClassifyEmailSpam,
            (_, _) => Task.FromException(new TimeoutException("the dependency did not answer")));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.store
            .ScheduleRetryAsync(
                job.JobId,
                job.Lease.Owner,
                Arg.Any<JobFailureRecord>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = this.ExecutorFor(
            new StubJobFailureClassifier(JobFailureClassification.Transient),
            handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal(JobExecutionOutcome.LeaseLost, result.Outcome);
        Assert.Null(result.AttemptFailure);
    }

    /// <summary>
    /// A handler works on mail, so a library's exception message may quote a subject, an address, or a header. What
    /// leaves the executor is the record the classifier produced and nothing else, which is what keeps the message out
    /// of every log line, counter, and span reporting the attempt.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_AHandlerWhoseFailureQuotesTheMail_ReportsOnlyTheClassifiedRecord()
    {
        // Arrange
        var raised = new InvalidOperationException("Re: your invoice from alex@example.test could not be parsed");
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam, (_, _) => Task.FromException(raised));
        var job = LeasedJobFor(JobType.ClassifyEmailSpam);
        this.AllowDeadLettering(job);

        var executor = this.ExecutorFor(
            new StubJobFailureClassifier(JobFailureClassification.Permanent, "InvalidOperationException"),
            handler);

        // Act
        var result = await executor.ExecuteAsync(job, CancellationToken.None);

        // Assert
        Assert.Equal("InvalidOperationException", result.AttemptFailure?.Record.Reason);
        Assert.DoesNotContain(
            "example.test",
            result.AttemptFailure?.Record.Reason ?? string.Empty,
            StringComparison.Ordinal);
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
        Assert.Null(result.AttemptFailure);
    }

    private static LeasedJob LeasedJobFor(JobType jobType, int attemptCount = 1) => new(
        JobId.Create(Guid.CreateVersion7(Noon)),
        jobType,
        JobIdempotencyKey.Create("account-a/inbox/1/42"),
        new ClassifyEmailSpamJobPayload
        {
            OwnerId = SyntheticMailOwner.Deployment.Value,
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = 42,
        },
        AccountId: null,
        attemptCount,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon + LeaseDuration),
        EnqueuedTrace: null);

    /// <summary>Lets the lease keep being renewed, which is what a healthy long execution sees.</summary>
    private void AllowLeaseRenewal(LeasedJob job) => this.store
        .RenewLeaseAsync(job.JobId, job.Lease.Owner, LeaseDuration, Arg.Any<CancellationToken>())
        .Returns(Task.FromResult<JobLease?>(new JobLease(job.Lease.Owner, Noon + LeaseDuration + LeaseDuration)));

    private void AllowRetryScheduling(LeasedJob job) => this.store
        .ScheduleRetryAsync(
            job.JobId,
            job.Lease.Owner,
            Arg.Any<JobFailureRecord>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
        .Returns(true);

    private void AllowDeadLettering(LeasedJob job) => this.store
        .DeadLetterAsync(job.JobId, job.Lease.Owner, Arg.Any<JobFailureRecord>(), Arg.Any<CancellationToken>())
        .Returns(true);

    private JobExecutor ExecutorFor(params IJobHandler[] handlers) =>
        this.ExecutorFor(new StubJobFailureClassifier(JobFailureClassification.Permanent), handlers);

    private JobExecutor ExecutorFor(IJobFailureClassifier failureClassifier, params IJobHandler[] handlers) => new(
        this.store,
        new JobHandlerRegistry(handlers),
        failureClassifier,
        JobExecutionSettings.Create(
            batchSize: 5,
            LeaseDuration,
            ExecutionTimeout,
            MaxAttempts,
            RetryBaseDelay,
            RetryMaxDelay),
        this.timeProvider);
}

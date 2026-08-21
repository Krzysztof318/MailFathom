// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Execution;

public sealed class JobQueuePassTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(9);
    private const int BatchSize = 3;
    private const int MaxQueueDepthPerType = 100;

    private readonly FakeTimeProvider timeProvider = new(Noon);
    private readonly IJobStore store = Substitute.For<IJobStore>();
    private JobConcurrencyGate? concurrency;

    /// <summary>
    /// A process with no handler would claim under a filter naming no type, so it claims nothing at all — which is also
    /// what leaves work an older replica cannot run for a newer one.
    /// </summary>
    [Fact]
    public async Task RunAsync_NoRegisteredHandler_ClaimsNothing()
    {
        // Arrange
        var pass = this.PassFor();

        // Act
        var results = await pass.RunAsync(CancellationToken.None);

        // Assert
        Assert.Empty(results);
        await this.store.DidNotReceive().ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The claim is filtered to what this build can run and bounded by what one pass may take.</summary>
    [Fact]
    public async Task RunAsync_ARegisteredHandler_ClaimsUnderTheHandledTypesAndTheConfiguredBounds()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns([]);

        var pass = this.PassFor(handler);

        // Act
        await pass.RunAsync(CancellationToken.None);

        // Assert
        await this.store.Received(1).ClaimAsync(
            Arg.Is<JobClaimRequest>(request =>
                request != null
                && request.HandledTypes.Count == 1
                && request.HandledTypes[0] == JobType.ClassifyEmailSpam
                && request.BatchSize == BatchSize
                && request.LeaseDuration == LeaseDuration),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Every job the claim handed over is run, and each one is reported on its own.</summary>
    [Fact]
    public async Task RunAsync_ABatchOfClaimedJobs_RunsEachOfThemAndReportsOneResultPerJob()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var claimedJobs = Enumerable.Range(0, 3).Select(LeasedJobFor).ToArray();

        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimedJobs);
        this.store
            .CompleteAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var pass = this.PassFor(handler);

        // Act
        var results = await pass.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(3, handler.RunCount);
        Assert.Equal(
            [.. claimedJobs.Select(job => job.JobId)],
            [.. results.Select(result => result.JobId)]);
        Assert.All(results, result => Assert.Equal(JobExecutionOutcome.Succeeded, result.Outcome));
    }

    /// <summary>An empty queue is the ordinary state of an instance, so a claim that took nothing is not an event.</summary>
    [Fact]
    public async Task RunAsync_NothingDue_ReportsNothing()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns([]);

        var pass = this.PassFor(handler);

        // Act
        var results = await pass.RunAsync(CancellationToken.None);

        // Assert
        Assert.Empty(results);
        Assert.Equal(0, handler.RunCount);
    }

    /// <summary>
    /// A batch claimed just before a shutdown gives every one of its leases back, so nothing waits out an expiry for
    /// work a stopping host never started.
    /// </summary>
    [Fact]
    public async Task RunAsync_AHostAlreadyStopping_ReleasesEveryClaimedJobWithoutRunningAny()
    {
        // Arrange
        var handler = new RecordingJobHandler(JobType.ClassifyEmailSpam);
        var claimedJobs = Enumerable.Range(0, 2).Select(LeasedJobFor).ToArray();

        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimedJobs);
        this.store
            .ReleaseAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        using var stoppingSource = new CancellationTokenSource();
        await stoppingSource.CancelAsync();

        var pass = this.PassFor(handler);

        // Act
        var results = await pass.RunAsync(stoppingSource.Token);

        // Assert
        Assert.Equal(0, handler.RunCount);
        Assert.All(results, result => Assert.Equal(JobExecutionOutcome.ReleasedForShutdown, result.Outcome));
        await this.store.Received(2).ReleaseAsync(
            Arg.Any<JobId>(),
            Arg.Any<JobLeaseOwner>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A batch is what one claim took, not what may run: the jobs beyond the process ceiling wait for a slot, which is
    /// what keeps background work from taking the capacity a mail synchronization and an MCP read also need.
    /// </summary>
    [Fact]
    public async Task RunAsync_MoreClaimedJobsThanTheProcessCeiling_RunsNoMoreThanTheCeilingAtOnce()
    {
        // Arrange
        const int processCeiling = 2;
        var handler = new ConcurrencyObservingJobHandler(JobType.ClassifyEmailSpam, processCeiling);
        var claimedJobs = Enumerable.Range(0, 3).Select(LeasedJobFor).ToArray();

        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimedJobs);
        this.store
            .CompleteAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var capacity = JobCapacitySettings.Create(processCeiling, processCeiling, MaxQueueDepthPerType);
        var pass = this.PassFor(capacity, handler);

        // Act
        var results = await pass.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(processCeiling, handler.PeakConcurrency);
        Assert.Equal(claimedJobs.Length, handler.RunCount);
        Assert.Equal(claimedJobs.Length, results.Count);
    }

    /// <summary>
    /// The per-type ceiling binds below the process ceiling, which is what stops one consumer's backlog from occupying
    /// every slot another consumer's work would have run in.
    /// </summary>
    [Fact]
    public async Task RunAsync_ABatchOfOneTypeUnderATighterPerTypeCeiling_RunsNoMoreOfThatTypeThanItAllows()
    {
        // Arrange
        const int perTypeCeiling = 1;
        var handler = new ConcurrencyObservingJobHandler(JobType.ClassifyEmailSpam, perTypeCeiling);
        var claimedJobs = Enumerable.Range(0, 3).Select(LeasedJobFor).ToArray();

        this.store.ClaimAsync(Arg.Any<JobClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimedJobs);
        this.store
            .CompleteAsync(Arg.Any<JobId>(), Arg.Any<JobLeaseOwner>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var capacity = JobCapacitySettings.Create(claimedJobs.Length, perTypeCeiling, MaxQueueDepthPerType);
        var pass = this.PassFor(capacity, handler);

        // Act
        await pass.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(perTypeCeiling, handler.PeakConcurrency);
        Assert.Equal(claimedJobs.Length, handler.RunCount);
    }

    /// <inheritdoc />
    public void Dispose() => this.concurrency?.Dispose();

    private static LeasedJob LeasedJobFor(int uid) => new(
        JobId.Create(Guid.CreateVersion7(Noon.AddSeconds(uid))),
        JobType.ClassifyEmailSpam,
        JobIdempotencyKey.Create($"account-a/inbox/1/{uid}"),
        new ClassifyEmailSpamJobPayload
        {
            AccountId = "account-a",
            FolderAlias = "inbox",
            FolderResolutionGeneration = 1,
            UidValidity = 1,
            Uid = (uint)(uid + 1),
        },
        AccountId: null,
        AttemptCount: 1,
        new JobLease(JobLeaseOwner.Create("attempt-a"), Noon + LeaseDuration),
        EnqueuedTrace: null);

    private JobQueuePass PassFor(params IJobHandler[] handlers) =>
        this.PassFor(JobCapacitySettings.Create(BatchSize, BatchSize, MaxQueueDepthPerType), handlers);

    private JobQueuePass PassFor(JobCapacitySettings capacity, params IJobHandler[] handlers)
    {
        var settings = JobExecutionSettings.Create(
            BatchSize,
            LeaseDuration,
            ExecutionTimeout,
            maxAttempts: 5,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(30));
        var registry = new JobHandlerRegistry(handlers);
        var failureClassifier = new StubJobFailureClassifier(JobFailureClassification.Permanent);
        var executor = new JobExecutor(this.store, registry, failureClassifier, settings, this.timeProvider);

        this.concurrency = new JobConcurrencyGate(capacity);

        return new JobQueuePass(
            this.store,
            registry,
            new DirectJobAttemptRunner(executor),
            this.concurrency,
            settings);
    }
}

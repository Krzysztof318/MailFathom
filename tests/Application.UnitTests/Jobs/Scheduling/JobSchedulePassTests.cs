// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Jobs.Scheduling;

/// <summary>What one pass does about a schedule: seeding it, dispatching it, and passing over what it missed.</summary>
public sealed class JobSchedulePassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly JobScheduleId ScheduleId = JobScheduleId.Create("mail-rules:personal:housekeeping");

    private readonly InMemoryJobScheduleStore schedules = new();
    private readonly IJobStore jobs = Substitute.For<IJobStore>();
    private readonly FakeTimeProvider clock = new(Instant("2026-08-13T06:00:00Z"));

    /// <summary>A schedule seen for the first time counts from now, so adding one at noon does not fire last night's occasion.</summary>
    [Fact]
    public async Task RunAsync_AScheduleSeenForTheFirstTime_CountsFromNowAndDispatchesNothing()
    {
        // Arrange
        var pass = this.CreatePass("Daily at 03:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var dispatch = Assert.Single(dispatches);
        Assert.Equal(JobScheduleDispatchOutcome.Seeded, dispatch.Outcome);
        Assert.Equal(this.clock.GetUtcNow(), this.schedules.Find(ScheduleId)?.ObservedFrom);
        await this.jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Between its occasions a schedule does nothing at all, which is the ordinary state of every schedule.</summary>
    [Fact]
    public async Task RunAsync_NoOccasionSinceTheLastOne_LeavesTheScheduleAlone()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T03:00:00Z")));
        var pass = this.CreatePass("Daily at 03:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(JobScheduleDispatchOutcome.NotDue, Assert.Single(dispatches).Outcome);
        await this.jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An occasion that has passed reaches the queue under an identity composed of the schedule and the instant.</summary>
    [Fact]
    public async Task RunAsync_AnOccasionThatHasPassed_EnqueuesItUnderTheOccasionsOwnIdentity()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-12T03:00:00Z")));
        this.ArrangeEnqueue(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7())));
        var pass = this.CreatePass("Daily at 03:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var dispatch = Assert.Single(dispatches);
        Assert.Equal(JobScheduleDispatchOutcome.Dispatched, dispatch.Outcome);
        Assert.Equal(Instant("2026-08-13T03:00:00Z"), dispatch.OccurrenceAt);
        Assert.Equal(0, dispatch.SkippedOccurrenceCount);
        await this.jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request =>
                request != null
                && request.Key.Value == "mail-rules:personal:housekeeping@2026-08-13T03:00:00Z"
                && request.JobType == JobType.RunScheduledMailRules),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Occasions that passed while the instance was down are skipped rather than replayed, and how many is reported.</summary>
    [Fact]
    public async Task RunAsync_OccasionsThatPassedWhileTheInstanceWasDown_RunsOnlyTheLatestAndCountsTheRest()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T00:00:00Z")));
        this.ArrangeEnqueue(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7())));
        var pass = this.CreatePass("Every 01:00:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var dispatch = Assert.Single(dispatches);
        Assert.Equal(JobScheduleDispatchOutcome.Dispatched, dispatch.Outcome);
        Assert.Equal(Instant("2026-08-13T06:00:00Z"), dispatch.OccurrenceAt);
        Assert.Equal(5, dispatch.SkippedOccurrenceCount);
        await this.jobs.Received(1).EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The schedule still advances past a skipped occasion, so the next pass does not offer it again.</summary>
    [Fact]
    public async Task RunAsync_AnOccasionThatWasDispatched_AdvancesTheScheduleToIt()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T00:00:00Z")));
        var jobId = JobId.Create(Guid.CreateVersion7());
        this.ArrangeEnqueue(JobEnqueueResult.Created(jobId));
        var pass = this.CreatePass("Every 01:00:00");

        // Act
        await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var state = this.schedules.Find(ScheduleId);
        Assert.Equal(Instant("2026-08-13T06:00:00Z"), state?.LastOccurrenceAt);
        Assert.Equal(jobId, state?.LastDispatchedJobId);
    }

    /// <summary>Two replicas reaching one occasion compose one identity, so the second is answered with the first's job.</summary>
    [Fact]
    public async Task RunAsync_AnOccasionAnotherReplicaAlreadyEnqueued_IsAnsweredWithThatJob()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-12T03:00:00Z")));
        this.ArrangeEnqueue(JobEnqueueResult.AlreadyEnqueued(JobId.Create(Guid.CreateVersion7())));
        var pass = this.CreatePass("Daily at 03:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(JobScheduleDispatchOutcome.AlreadyDispatched, Assert.Single(dispatches).Outcome);
    }

    /// <summary>Work that outlasts its own interval is not queued a second time; the occasion is answered and passed over.</summary>
    [Theory]
    [InlineData(JobState.Pending)]
    [InlineData(JobState.Claimed)]
    public async Task RunAsync_ThePreviousRunStillInFlight_AnswersTheOccasionRatherThanStartingIt(JobState inFlight)
    {
        // Arrange
        var previousJobId = JobId.Create(Guid.CreateVersion7());
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T00:00:00Z")) with { LastDispatchedJobId = previousJobId });
        this.jobs.FindStateAsync(previousJobId, Arg.Any<CancellationToken>()).Returns(inFlight);
        var pass = this.CreatePass("Every 01:00:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var dispatch = Assert.Single(dispatches);
        Assert.Equal(JobScheduleDispatchOutcome.PreviousRunInFlight, dispatch.Outcome);
        Assert.Equal(6, dispatch.SkippedOccurrenceCount);
        Assert.Equal(Instant("2026-08-13T06:00:00Z"), this.schedules.Find(ScheduleId)?.LastOccurrenceAt);
        await this.jobs.DidNotReceive().EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A previous run that finished holds nothing up, which is what keeps a schedule going after its first occasion.</summary>
    [Fact]
    public async Task RunAsync_ThePreviousRunAlreadyFinished_DispatchesTheOccasion()
    {
        // Arrange
        var previousJobId = JobId.Create(Guid.CreateVersion7());
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T05:00:00Z")) with { LastDispatchedJobId = previousJobId });
        this.jobs.FindStateAsync(previousJobId, Arg.Any<CancellationToken>()).Returns(JobState.Succeeded);
        this.ArrangeEnqueue(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7())));
        var pass = this.CreatePass("Every 01:00:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(JobScheduleDispatchOutcome.Dispatched, Assert.Single(dispatches).Outcome);
    }

    /// <summary>A full queue is backpressure rather than a debt: the occasion is passed over and counted, and the schedule moves on.</summary>
    [Fact]
    public async Task RunAsync_TheQueueAtItsDepthBound_PassesTheOccasionOverAndCountsIt()
    {
        // Arrange
        this.schedules.Arrange(StateAfter(Instant("2026-08-13T05:00:00Z")));
        this.ArrangeEnqueue(JobEnqueueResult.RefusedAtCapacity());
        var pass = this.CreatePass("Every 01:00:00");

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        var dispatch = Assert.Single(dispatches);
        Assert.Equal(JobScheduleDispatchOutcome.RefusedAtCapacity, dispatch.Outcome);
        Assert.Equal(1, dispatch.SkippedOccurrenceCount);
        Assert.Equal(Instant("2026-08-13T06:00:00Z"), this.schedules.Find(ScheduleId)?.LastOccurrenceAt);
    }

    /// <summary>A deployment declaring no schedule costs nothing at all, which is what every deployment did before schedules existed.</summary>
    [Fact]
    public async Task RunAsync_NoScheduleDeclared_ReadsNothingAndDispatchesNothing()
    {
        // Arrange
        var pass = new JobSchedulePass([new StubScheduledJobSource([])], this.schedules, this.jobs, this.clock);

        // Act
        var dispatches = await pass.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(dispatches);
        Assert.Empty(this.schedules.Saves);
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static JobScheduleState StateAfter(DateTimeOffset occurrence) => new()
    {
        Id = ScheduleId,
        ObservedFrom = Instant("2026-08-01T00:00:00Z"),
        LastOccurrenceAt = occurrence,
    };

    private static JobRecurrence Parse(string declaration)
    {
        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out _));

        return recurrence!;
    }

    private void ArrangeEnqueue(JobEnqueueResult result) =>
        this.jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>()).Returns(result);

    private JobSchedulePass CreatePass(string declaration) => new(
        [
            new StubScheduledJobSource(
            [
                new ScheduledJob(ScheduleId, RunScheduledMailRulesJobPayload.For(Account), Parse(declaration), Account),
            ]),
        ],
        this.schedules,
        this.jobs,
        this.clock);

    /// <summary>Declares a fixed set of schedules, which is what a configuration-backed source does to a pass.</summary>
    private sealed class StubScheduledJobSource(IReadOnlyList<ScheduledJob> declared) : IScheduledJobSource
    {
        public Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(declared);
    }
}

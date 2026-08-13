// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>Enqueues the work whose occasion has passed, once per occasion, and passes over the occasions that were missed.</summary>
/// <remarks>
/// <para>
/// This is dispatch and not a scheduler. It owns no loop, no timer, and no queue: it is one step the worker already
/// polling the job queue takes before it claims, so recurring work reaches the same worker, the same concurrency
/// ceiling, the same queue depth bound, and the same retry and dead-letter path as work an event enqueued. A schedule
/// therefore adds an occasion to the existing mechanism rather than a second mechanism beside it.
/// </para>
/// <para>
/// <strong>A missed occasion is skipped rather than replayed.</strong> Only the most recent occasion at or before now is
/// ever enqueued, so an instance that was down for a week comes back and runs the work once instead of beginning with a
/// week of catch-up passes over somebody's mailbox. How many were passed over is counted and reported, because a burst
/// that was deliberately not run must not look like a schedule that kept up.
/// </para>
/// <para>
/// <strong>One run per schedule at a time.</strong> The work a schedule repeats can outlast its own interval, so the
/// job the previous occasion enqueued is asked about first: while it is still pending or held, this occasion is answered
/// rather than started, and the schedule advances past it. That is the same answer the whole-mailbox rule run gives a
/// second request, and it is what keeps a slow pass from queuing a copy of itself every interval.
/// </para>
/// <para>
/// Nothing here is exclusive between replicas, and it does not need to be. The identity of an execution is the schedule
/// and the occasion together, so two instances reaching one occasion compose one key and the queue's own uniqueness
/// answers the second with the job the first wrote.
/// </para>
/// </remarks>
public sealed class JobSchedulePass
{
    private readonly IScheduledJobSource schedules;
    private readonly IJobScheduleStore scheduleStore;
    private readonly IJobStore jobStore;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass from the schedules it reads and the queue it dispatches into.</summary>
    /// <param name="schedules">Declares which recurring dispatches this instance has.</param>
    /// <param name="scheduleStore">Keeps what each schedule has already dispatched.</param>
    /// <param name="jobStore">Enqueues the occasion and answers what became of the previous one.</param>
    /// <param name="timeProvider">Supplies the instant the occasions are read against.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public JobSchedulePass(
        IScheduledJobSource schedules,
        IJobScheduleStore scheduleStore,
        IJobStore jobStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        ArgumentNullException.ThrowIfNull(scheduleStore);
        ArgumentNullException.ThrowIfNull(jobStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.schedules = schedules;
        this.scheduleStore = scheduleStore;
        this.jobStore = jobStore;
        this.timeProvider = timeProvider;
    }

    /// <summary>Decides about every declared schedule, and enqueues the ones whose occasion has passed.</summary>
    /// <param name="cancellationToken">Cancels the pass between schedules.</param>
    /// <returns>One decision per declared schedule, in declared order, and an empty answer when none is declared.</returns>
    /// <remarks>
    /// The schedules are decided one after another rather than together, because each is one read, one enqueue, and one
    /// small write, and a deployment declares as many of them as it has rules — a count where doing them in order costs
    /// nothing and keeps the queue's depth bound reached in a defined order.
    /// </remarks>
    public async Task<IReadOnlyList<JobScheduleDispatch>> RunAsync(CancellationToken cancellationToken)
    {
        var declared = this.schedules.ReadSchedules();

        if (declared.Count == 0)
        {
            return [];
        }

        var states = await this.scheduleStore.ReadAsync(
            [.. declared.Select(schedule => schedule.Id)],
            cancellationToken);

        var dispatches = new List<JobScheduleDispatch>(declared.Count);

        foreach (var schedule in declared)
        {
            cancellationToken.ThrowIfCancellationRequested();

            states.TryGetValue(schedule.Id.Value, out var state);

            dispatches.Add(await this.DecideAsync(schedule, state, cancellationToken));
        }

        return dispatches;
    }

    /// <summary>Decides about one schedule and writes down whatever the decision moved.</summary>
    private async Task<JobScheduleDispatch> DecideAsync(
        ScheduledJob schedule,
        JobScheduleState? state,
        CancellationToken cancellationToken)
    {
        var now = this.timeProvider.GetUtcNow();

        if (state is null)
        {
            var seeded = new JobScheduleState { Id = schedule.Id, ObservedFrom = now };

            await this.scheduleStore.SaveAsync(seeded, cancellationToken);

            return Report(schedule, JobScheduleDispatchOutcome.Seeded, occurrenceAt: null, skippedCount: 0);
        }

        if (schedule.Recurrence.LatestOccurrenceAtOrBefore(now) is not { } occurrence
            || occurrence <= state.CountedFrom)
        {
            return Report(schedule, JobScheduleDispatchOutcome.NotDue, occurrenceAt: null, skippedCount: 0);
        }

        // Every occasion this pass is about to step over, which is the one being dispatched excluded from the window
        // between what the schedule last accounted for and now.
        var skippedCount = schedule.Recurrence.CountOccurrencesIn(state.CountedFrom, occurrence) - 1;

        if (await this.IsPreviousRunInFlightAsync(state, cancellationToken))
        {
            await this.scheduleStore.SaveAsync(state with { LastOccurrenceAt = occurrence }, cancellationToken);

            return Report(
                schedule,
                JobScheduleDispatchOutcome.PreviousRunInFlight,
                occurrence,
                skippedCount + 1);
        }

        var enqueued = await this.jobStore.EnqueueAsync(
            JobEnqueueRequest.Create(ComposeKey(schedule.Id, occurrence), schedule.Payload, schedule.AccountId),
            cancellationToken);

        await this.scheduleStore.SaveAsync(
            state with
            {
                LastOccurrenceAt = occurrence,
                LastDispatchedJobId = enqueued.JobId ?? state.LastDispatchedJobId,
            },
            cancellationToken);

        return enqueued.Outcome switch
        {
            JobEnqueueOutcome.Created =>
                Report(schedule, JobScheduleDispatchOutcome.Dispatched, occurrence, skippedCount),
            JobEnqueueOutcome.AlreadyEnqueued =>
                Report(schedule, JobScheduleDispatchOutcome.AlreadyDispatched, occurrence, skippedCount),
            _ => Report(schedule, JobScheduleDispatchOutcome.RefusedAtCapacity, occurrence, skippedCount + 1),
        };
    }

    /// <summary>Composes the identity of one occasion's execution, which is the schedule and the instant together.</summary>
    /// <remarks>
    /// The instant is written to the second in UTC, so the same occasion composes the same key on every replica and in
    /// every locale. Uniqueness against the queue is what makes a second dispatch of one occasion an answer rather than
    /// a second execution, so the key has to name the occasion and not the moment somebody noticed it.
    /// </remarks>
    private static JobIdempotencyKey ComposeKey(JobScheduleId id, DateTimeOffset occurrence) => JobIdempotencyKey.Create(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{id.Value}@{occurrence.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}"));

    /// <summary>Answers whether the job the schedule last enqueued is still on its way.</summary>
    /// <remarks>
    /// A job the queue no longer has is not in flight. That is the case a pruned or manually removed row produces, and
    /// treating it as still running would stop the schedule forever over a row nobody can point at.
    /// </remarks>
    private async Task<bool> IsPreviousRunInFlightAsync(JobScheduleState state, CancellationToken cancellationToken)
    {
        if (state.LastDispatchedJobId is not { } jobId)
        {
            return false;
        }

        var jobState = await this.jobStore.FindStateAsync(jobId, cancellationToken);

        return jobState is JobState.Pending or JobState.Claimed;
    }

    private static JobScheduleDispatch Report(
        ScheduledJob schedule,
        JobScheduleDispatchOutcome outcome,
        DateTimeOffset? occurrenceAt,
        int skippedCount) => new(
        schedule.Id,
        schedule.Payload.JobType,
        outcome,
        occurrenceAt,
        Math.Max(skippedCount, 0));
}

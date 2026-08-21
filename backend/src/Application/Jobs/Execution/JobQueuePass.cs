// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Takes one batch of due jobs this process can run, and runs them under the capacity the instance allows.</summary>
/// <remarks>
/// <para>
/// One claim and one owner per pass. The owner identifies the attempt rather than the process, which is what lets a
/// write be refused once the lease has moved on, and a batch shares one because the claim that stamped them is one
/// statement.
/// </para>
/// <para>
/// How many of a batch run at once is not the batch's business: the batch is what one claim took, and the ceiling is
/// what the instance may spend on background work. Every claimed job is therefore dispatched at once and each waits for
/// <see cref="JobConcurrencyGate" /> to let it through, so raising the batch size buys fewer round trips rather than
/// more work in flight.
/// </para>
/// <para>
/// A process with no handler claims nothing at all, rather than claiming and abandoning. That is the ordinary state of
/// a build whose consumers have not arrived, and it is also what makes a rolling deployment safe: work an older replica
/// cannot run stays where it is for a newer one.
/// </para>
/// </remarks>
public sealed class JobQueuePass
{
    private readonly IJobStore store;
    private readonly JobHandlerRegistry handlers;
    private readonly IJobAttemptRunner attemptRunner;
    private readonly JobConcurrencyGate concurrency;
    private readonly JobExecutionSettings settings;

    /// <summary>Initializes the pass from the queue it claims out of and the bounds it claims and runs under.</summary>
    /// <param name="store">Claims the batch this pass runs.</param>
    /// <param name="handlers">Answers which job types this process can run.</param>
    /// <param name="attemptRunner">Runs one claimed job, isolated from the jobs running beside it, and records its outcome.</param>
    /// <param name="concurrency">Decides how many of the batch run at once, in total and per job type.</param>
    /// <param name="settings">The batch size and the lease each claimed job is held under.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public JobQueuePass(
        IJobStore store,
        JobHandlerRegistry handlers,
        IJobAttemptRunner attemptRunner,
        JobConcurrencyGate concurrency,
        JobExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(attemptRunner);
        ArgumentNullException.ThrowIfNull(concurrency);
        ArgumentNullException.ThrowIfNull(settings);

        this.store = store;
        this.handlers = handlers;
        this.attemptRunner = attemptRunner;
        this.concurrency = concurrency;
        this.settings = settings;
    }

    /// <summary>Claims one batch and runs it, reporting what each job did.</summary>
    /// <param name="stoppingToken">Cancels the claim, and stops each running job so its lease is given back.</param>
    /// <returns>One result per claimed job, in the order they were claimed, and an empty answer when nothing was due.</returns>
    /// <remarks>
    /// Cancellation does not end the pass early. Every claimed job is still dispatched, because a job the runner finds
    /// already cancelled is released rather than run — which is how the jobs behind the ones that were running get their
    /// leases back instead of waiting out an expiry.
    /// </remarks>
    public async Task<IReadOnlyList<JobExecutionResult>> RunAsync(CancellationToken stoppingToken)
    {
        if (this.handlers.HandledTypes.Count == 0)
        {
            return [];
        }

        var request = JobClaimRequest.Create(
            this.handlers.HandledTypes,
            this.settings.BatchSize,
            this.settings.LeaseDuration,
            JobLeaseOwner.NewAttempt());

        var claimedJobs = await this.store.ClaimAsync(request, stoppingToken);

        var attempts = claimedJobs
            .Select(claimedJob => this.RunWithinCapacityAsync(claimedJob, stoppingToken))
            .ToArray();

        return await Task.WhenAll(attempts);
    }

    /// <summary>Waits for the capacity one job needs, runs it, and gives that capacity back however it ended.</summary>
    /// <remarks>
    /// The wait itself is not cancellable, deliberately. A stopping host still has to reach every claimed job, because
    /// reaching it is what releases its lease, and a wait abandoned on shutdown would leave the jobs behind the running
    /// ones held until their leases expired. Nothing waits indefinitely for that to be safe: each attempt ahead is
    /// already bounded by the execution timeout, and a stopping host cancels the ones in flight at once.
    /// </remarks>
    private async Task<JobExecutionResult> RunWithinCapacityAsync(LeasedJob job, CancellationToken stoppingToken)
    {
        using var capacity = await this.concurrency.AcquireAsync(job.JobType, CancellationToken.None);

        return await this.attemptRunner.RunAsync(job, stoppingToken);
    }
}

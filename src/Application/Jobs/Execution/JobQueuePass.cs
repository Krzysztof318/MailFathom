// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Execution;

/// <summary>Takes one batch of due jobs this process can run, and runs each of them in turn.</summary>
/// <remarks>
/// <para>
/// One claim and one owner per pass. The owner identifies the attempt rather than the process, which is what lets a
/// write be refused once the lease has moved on, and a batch shares one because the claim that stamped them is one
/// statement.
/// </para>
/// <para>
/// The jobs run one after another. How many may run at once is a bound this pass does not express, so running them in
/// sequence is what keeps a batch from becoming an unstated concurrency limit of its own.
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
    private readonly JobExecutor executor;
    private readonly JobExecutionSettings settings;

    /// <summary>Initializes the pass from the queue it claims out of and the bounds it claims under.</summary>
    /// <param name="store">Claims the batch this pass runs.</param>
    /// <param name="handlers">Answers which job types this process can run.</param>
    /// <param name="executor">Runs one claimed job and records its outcome.</param>
    /// <param name="settings">The batch size and the lease each claimed job is held under.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public JobQueuePass(
        IJobStore store,
        JobHandlerRegistry handlers,
        JobExecutor executor,
        JobExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(settings);

        this.store = store;
        this.handlers = handlers;
        this.executor = executor;
        this.settings = settings;
    }

    /// <summary>Claims one batch and runs it, reporting what each job did.</summary>
    /// <param name="stoppingToken">Cancels the claim, and stops each running job so its lease is given back.</param>
    /// <returns>One result per claimed job, in the order they were run, and an empty answer when nothing was due.</returns>
    /// <remarks>
    /// Cancellation does not end the pass early. Every claimed job is still dispatched, because a job the executor
    /// finds already cancelled is released rather than run — which is how the jobs behind the one that was running get
    /// their leases back instead of waiting out an expiry.
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

        var results = new List<JobExecutionResult>(claimedJobs.Count);

        foreach (var claimedJob in claimedJobs)
        {
            results.Add(await this.executor.ExecuteAsync(claimedJob, stoppingToken));
        }

        return results;
    }
}

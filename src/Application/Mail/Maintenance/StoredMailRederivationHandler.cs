// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Carries one segment of a re-derivation: bounded passes over the scope's stored mail, for as long as the attempt lasts.</summary>
/// <remarks>
/// <para>
/// The deployment walks the mailbox rather than the operator's terminal, which is the whole of what this job type
/// exists for. An attempt runs pass after pass while its lease is renewed underneath it, and each pass commits what it
/// re-read together with the position it reached — so an attempt stopped by the execution timeout, by a shutdown, or by
/// a lease that moved on leaves durable work behind and nothing has to be walked twice.
/// </para>
/// <para>
/// What it does not do is hold a worker for as long as a mailbox takes. An attempt that is stopped with mail still
/// ahead of it hands the rest of the walk to a segment of its own: the run's segment count moves on, and the job that
/// carries it is enqueued under the key that count names. That is what keeps a walk of tens of thousands of messages
/// inside the queue's own bounds instead of turning an execution timeout into a failure of ordinary work.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once, which is what the queue asks of every handler. The
/// pass writes the same reading of the same immutable bytes, the position is committed with the work it accounts for,
/// and the counts are added to a record that is re-read inside the commit that advances it — so a second attempt that
/// overlapped the first neither loses its progress nor counts it twice.
/// </para>
/// </remarks>
public sealed class StoredMailRederivationHandler : IJobHandler
{
    private readonly StoredMailRederivation rederivation;
    private readonly IStoredMailRederivationRunStore runStore;
    private readonly IJobStore jobs;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IStoredMailRederivationTelemetry telemetry;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the handler from the walk it drives and the record it keeps.</summary>
    /// <param name="rederivation">Runs one bounded pass over the scope's stored mail.</param>
    /// <param name="runStore">Holds the run the segments are carrying, and how far it has come.</param>
    /// <param name="jobs">Enqueues the segment that carries whatever this attempt did not reach.</param>
    /// <param name="commitPolicy">Advances the run from a fresh read, resolving a race with an overlapping attempt.</param>
    /// <param name="telemetry">Publishes the segment and the passes beneath it.</param>
    /// <param name="timeProvider">Stamps the instant the run reached the end of its scope.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public StoredMailRederivationHandler(
        StoredMailRederivation rederivation,
        IStoredMailRederivationRunStore runStore,
        IJobStore jobs,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IStoredMailRederivationTelemetry telemetry,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(rederivation);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.rederivation = rederivation;
        this.runStore = runStore;
        this.jobs = jobs;
        this.commitPolicy = commitPolicy;
        this.telemetry = telemetry;
        this.timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.RederiveStoredMail;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    /// <remarks>
    /// A segment whose scope has no outstanding run does nothing at all. That is what a job outliving the run it was
    /// enqueued for looks like — an overlapping attempt reached the end of the scope first, or the operator's request
    /// was answered by a run that has since finished — and it is an outcome rather than a failure: the work the segment
    /// was for is done.
    /// </remarks>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not StoredMailScopeJobPayload named)
        {
            throw new ArgumentException(
                $"A '{JobType.RederiveStoredMail}' job carries a payload naming one scope of stored mail.",
                nameof(payload));
        }

        StoredMailScope scope = new(named.ToAccountId(), named.ToFolderAlias());

        if (await this.runStore.FindAsync(scope, cancellationToken) is not { IsOutstanding: true })
        {
            return;
        }

        using var runScope = this.telemetry.BeginRun(scope.Account, scope.Folder);

        if (await this.WalkAsync(scope, runScope, cancellationToken))
        {
            runScope.ReachedEndOfScope();

            return;
        }

        await this.HandOnAsync(scope, named, runScope);
    }

    /// <summary>Runs passes until the scope is exhausted, the run ends beneath this attempt, or the attempt is stopped.</summary>
    /// <returns><see langword="true" /> when the run reached the end of its scope and was ended.</returns>
    /// <remarks>
    /// Cancellation is caught rather than raised, because being stopped is how an ordinary segment ends: the executor
    /// cancels the attempt at the execution timeout, at shutdown, and when the lease has moved on, and none of the
    /// three says the work failed. What the attempt owes afterwards is the segment that carries the rest, which is why
    /// the caller reaches it on this path as well.
    /// </remarks>
    private async Task<bool> WalkAsync(
        StoredMailScope scope,
        IStoredMailRederivationRunScope runScope,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                StoredMailRederivationPass pass;

                using (var passScope = runScope.BeginPass())
                {
                    pass = await this.rederivation.RunAsync(scope, cancellationToken);
                    passScope.Completed(pass);
                }

                var run = await this.RecordAsync(scope, pass, !pass.EmailsRemain);

                // The run is gone from under this attempt when an overlapping one reached the end of the scope first.
                // There is nothing left to carry and nothing to hand on, so the attempt ends as the one that finished.
                if (run is null || !pass.EmailsRemain)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Handed on by the caller: what the passes committed is durable, and the position they reached is what the
            // next segment resumes from.
        }

        return false;
    }

    /// <summary>Adds what one pass committed to the run, and ends the run when that pass reached the end of the scope.</summary>
    /// <returns>The run as it now stands, or <see langword="null" /> when the scope no longer has one outstanding.</returns>
    /// <remarks>
    /// The record is re-read inside the commit rather than advanced from what this attempt last saw, because two
    /// attempts can overlap for as long as it takes a lost lease to be noticed. Writing back a whole record read before
    /// the pass would drop whatever the other attempt committed in between, and a count that goes backwards is exactly
    /// what an operator watching a walk would read as work being lost.
    /// </remarks>
    private Task<StoredMailRederivationRun?> RecordAsync(
        StoredMailScope scope,
        StoredMailRederivationPass pass,
        bool reachedEndOfScope) =>
        this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(scope, attemptCancellationToken) is not { IsOutstanding: true } run)
                {
                    return null;
                }

                var advanced = run with
                {
                    RederivedEmailCount = run.RederivedEmailCount + pass.RederivedEmailCount,
                    UnreadableEmailCount = run.UnreadableEmailCount + pass.UnreadableEmailCount,
                    MissingContentEmailCount = run.MissingContentEmailCount + pass.MissingContentEmailCount,
                    EndedAt = reachedEndOfScope ? this.timeProvider.GetUtcNow() : run.EndedAt,
                };

                await this.runStore.SaveAsync(session, advanced, attemptCancellationToken);

                return advanced;
            },
            CancellationToken.None);

    /// <summary>Moves the run on to its next segment and enqueues the job that carries it.</summary>
    /// <remarks>
    /// Outside the attempt's cancellation, deliberately, and for the reason the worker records an outcome outside it:
    /// the one moment the rest of a walk most needs to be written down is the shutdown that stopped it, and a write
    /// cancelled by the token that stopped the handler would leave a run outstanding with nothing carrying it.
    /// <para>
    /// The segment is committed before it is enqueued, so a key is never handed to the queue before the run says which
    /// segment it is on. A crash between the two leaves the run one segment ahead of the queue, which the operator's
    /// next request repairs by enqueuing that same key — and an enqueue that arrives twice is answered with the job
    /// already there.
    /// </para>
    /// </remarks>
    private async Task HandOnAsync(
        StoredMailScope scope,
        StoredMailScopeJobPayload payload,
        IStoredMailRederivationRunScope runScope)
    {
        var next = await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(scope, attemptCancellationToken) is not { IsOutstanding: true } run)
                {
                    return null;
                }

                var advanced = run with { SegmentCount = run.SegmentCount + 1 };

                await this.runStore.SaveAsync(session, advanced, attemptCancellationToken);

                return advanced;
            },
            CancellationToken.None);

        if (next is null)
        {
            return;
        }

        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(
                StoredMailRederivationRequests.KeyOf(next),
                payload,
                scope.Account),
            CancellationToken.None);

        runScope.HandedOn(enqueued.Outcome is not JobEnqueueOutcome.RefusedAtCapacity);
    }
}

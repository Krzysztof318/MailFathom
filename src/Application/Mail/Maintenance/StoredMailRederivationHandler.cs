// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
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
/// pass writes the same reading of the same immutable bytes, and each batch commits the position it reached together
/// with what it read into the run — so a second attempt that overlapped the first neither loses its progress nor counts
/// it twice, and one killed between two batches leaves the run reporting exactly the mail that was really re-read.
/// </para>
/// </remarks>
public sealed class StoredMailRederivationHandler : IJobHandler
{
    private readonly StoredMailRederivation rederivation;
    private readonly IStoredMailRederivationRunStore runStore;
    private readonly IJobStore jobs;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IStoredMailRederivationTelemetry telemetry;

    /// <summary>Initializes the handler from the walk it drives and the record it keeps.</summary>
    /// <param name="rederivation">Runs one bounded pass over the scope's stored mail, advancing the run as it commits.</param>
    /// <param name="runStore">Reads whether the scope still has a run to carry, and moves it on to its next segment.</param>
    /// <param name="jobs">Enqueues the segment that carries whatever this attempt did not reach.</param>
    /// <param name="commitPolicy">Advances the segment from a fresh read, resolving a race with an overlapping attempt.</param>
    /// <param name="telemetry">Publishes the segment and the passes beneath it.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public StoredMailRederivationHandler(
        StoredMailRederivation rederivation,
        IStoredMailRederivationRunStore runStore,
        IJobStore jobs,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IStoredMailRederivationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(rederivation);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.rederivation = rederivation;
        this.runStore = runStore;
        this.jobs = jobs;
        this.commitPolicy = commitPolicy;
        this.telemetry = telemetry;
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
        if (payload is not RederiveStoredMailJobPayload named)
        {
            throw new ArgumentException(
                $"A '{JobType.RederiveStoredMail}' job carries a payload naming one scope of stored mail.",
                nameof(payload));
        }

        StoredMailScope scope = new(named.ToAccountId(), named.ToFolderAlias());

        if (await this.runStore.FindAsync(scope, cancellationToken) is not { IsOutstanding: true } run)
        {
            return;
        }

        using var runScope = this.telemetry.BeginRun(scope.Account, scope.Folder);

        if (await this.WalkAsync(run.RunId, scope, runScope, cancellationToken))
        {
            runScope.ReachedEndOfScope();

            return;
        }

        await this.HandOnAsync(run.RunId, scope, named, runScope);
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
        StoredMailRederivationRunId runId,
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
                    pass = await this.rederivation.RunAsync(runId, scope, cancellationToken);
                    passScope.Completed(pass);
                }

                if (!pass.EmailsRemain)
                {
                    return true;
                }

                // The run is gone from under this attempt when an overlapping one reached the end of the scope first,
                // and replaced when the operator has since asked for another. There is nothing left to carry and
                // nothing to hand on either way, so the attempt ends as the one that finished.
                if (await this.runStore.FindAsync(scope, cancellationToken) is not { IsOutstanding: true } current
                    || current.RunId != runId)
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
    /// <para>
    /// A run that has ended or been replaced by the time this runs reports the segment as having reached the end of the
    /// scope, exactly as the walk's own mid-loop check reports the same race. There is nothing to hand on and nothing
    /// went wrong, and a segment whose span ended with neither signal would be indistinguishable from one that stopped
    /// where nobody wrote down why.
    /// </para>
    /// </remarks>
    private async Task HandOnAsync(
        StoredMailRederivationRunId runId,
        StoredMailScope scope,
        RederiveStoredMailJobPayload payload,
        IStoredMailRederivationRunScope runScope)
    {
        var next = await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(scope, attemptCancellationToken) is not { IsOutstanding: true } run
                    || run.RunId != runId)
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
            runScope.ReachedEndOfScope();

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

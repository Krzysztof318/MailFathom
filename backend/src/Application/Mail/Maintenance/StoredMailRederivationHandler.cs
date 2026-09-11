// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Coordination;
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
/// A scope is walked by one segment at a time across every replica, because a segment walks only while it holds the
/// lease on the scope, as <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// decides for an operator-asked re-derivation. Which replica the operator's request reached decides nothing: the
/// request writes the run down, and whichever segment holds the scope's lease walks it. A segment that finds the scope
/// held reads no mail and hands the rest on as any stopped segment does, to a successor the queue releases only once a
/// holder that stopped renewing would have lost the scope.
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
    private readonly IWorkLeaseRunner leases;
    private readonly JobExecutionSettings jobSettings;
    private readonly TimeProvider timeProvider;
    private readonly IStoredMailRederivationTelemetry telemetry;

    /// <summary>Initializes the handler from the walk it drives and the record it keeps.</summary>
    /// <param name="rederivation">Runs one bounded pass over the scope's stored mail, advancing the run as it commits.</param>
    /// <param name="runStore">Reads whether the scope still has a run to carry, and moves it on to its next segment.</param>
    /// <param name="jobs">Enqueues the segment that carries whatever this attempt did not reach.</param>
    /// <param name="commitPolicy">Advances the segment from a fresh read, resolving a race with an overlapping attempt.</param>
    /// <param name="leases">Walks the scope only while this segment holds its lease.</param>
    /// <param name="jobSettings">Names how long a lease runs, which is how long a segment refused the scope defers its successor.</param>
    /// <param name="timeProvider">Stamps the instant a deferred successor becomes claimable.</param>
    /// <param name="telemetry">Publishes the segment and the passes beneath it.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public StoredMailRederivationHandler(
        StoredMailRederivation rederivation,
        IStoredMailRederivationRunStore runStore,
        IJobStore jobs,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IWorkLeaseRunner leases,
        JobExecutionSettings jobSettings,
        TimeProvider timeProvider,
        IStoredMailRederivationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(rederivation);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentNullException.ThrowIfNull(jobSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(telemetry);

        this.rederivation = rederivation;
        this.runStore = runStore;
        this.jobs = jobs;
        this.commitPolicy = commitPolicy;
        this.leases = leases;
        this.jobSettings = jobSettings;
        this.timeProvider = timeProvider;
        this.telemetry = telemetry;
    }

    /// <summary>How one segment's walk ended, which is what decides what it hands on and when.</summary>
    private enum SegmentWalk
    {
        /// <summary>The walk was stopped with mail still ahead of it, and the rest is handed on at once.</summary>
        Stopped = 0,

        /// <summary>The walk reached the end of its scope, or the run it carried is over, so nothing is handed on.</summary>
        ReachedEndOfScope = 1,

        /// <summary>Another holder had the scope, so this segment walked nothing and defers the successor it hands on.</summary>
        HeldElsewhere = 2,
    }

    /// <inheritdoc />
    public JobType JobType => JobType.RederiveStoredMail;

    /// <summary>Names the lease one scope's walk is held under.</summary>
    /// <param name="scope">The account, and the one folder of it, a run walks.</param>
    /// <returns>The scope every segment of a run over that mail asks for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Keyed exactly as the walk's position is, by the user, the account, and the folder, with <c>*</c> standing for the
    /// whole account. A walk of one folder and a walk of the whole account are two runs with two positions, so they are
    /// two leases as well.
    /// </para>
    /// <para>
    /// A scope the lease cannot carry — longer than it leaves room for, or an account identifier holding a control
    /// character — is named by the SHA-256 digest of the account and the folder instead, for the reason a supervised
    /// account is: configuration accepts identifiers far longer than a lease scope, and a scope that could not be
    /// composed would fail every segment of that run.
    /// </para>
    /// </remarks>
    public static WorkScope LeaseScopeOf(StoredMailScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var user = scope.Account.User.Value;
        var accountId = scope.Account.Id.Value;
        var folder = scope.Folder?.Value ?? "*";
        var readableScope = string.Create(CultureInfo.InvariantCulture, $"mail-rederivation/{user}/{accountId}/{folder}");

        if (readableScope.Length <= WorkScope.MaximumLength && !accountId.Any(char.IsControl))
        {
            return WorkScope.Create(readableScope);
        }

        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId}/{folder}")));

        return WorkScope.Create(string.Create(CultureInfo.InvariantCulture, $"mail-rederivation/{user}/sha256-{digest}"));
    }

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

        StoredMailScope scope = new(named.ToAccountIdentity(), named.ToFolderAlias());

        if (await this.runStore.FindAsync(scope, cancellationToken) is not { IsOutstanding: true } run)
        {
            return;
        }

        using var runScope = this.telemetry.BeginRun(scope.Account.Id, scope.Folder);

        var walk = await this.WalkUnderLeaseAsync(run, runScope, cancellationToken);

        if (walk is SegmentWalk.ReachedEndOfScope)
        {
            runScope.ReachedEndOfScope();

            return;
        }

        DateTimeOffset? successorAvailableAt = walk is SegmentWalk.HeldElsewhere
            ? this.timeProvider.GetUtcNow() + this.jobSettings.LeaseDuration
            : null;

        await this.HandOnAsync(run, named, runScope, successorAvailableAt);
    }

    /// <summary>Walks the scope for as long as this segment holds its lease, and says how the walk ended.</summary>
    /// <remarks>
    /// A stop that lands while the lease is still being asked for is a stop like any other rather than a refusal: the
    /// claim may have been granted before it, and a refusal is what defers the successor, so reading it as one would
    /// leave a walk nobody else holds idle for a lease's length.
    /// </remarks>
    private async Task<SegmentWalk> WalkUnderLeaseAsync(
        StoredMailRederivationRun run,
        IStoredMailRederivationRunScope runScope,
        CancellationToken cancellationToken)
    {
        var reachedEndOfScope = false;

        try
        {
            var held = await this.leases.TryRunUnderLeaseAsync(
                LeaseScopeOf(run.Scope),
                async walkToken => reachedEndOfScope = await this.WalkAsync(run.RunId, run.Scope, runScope, walkToken),
                cancellationToken);

            if (!held)
            {
                return SegmentWalk.HeldElsewhere;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SegmentWalk.Stopped;
        }

        return reachedEndOfScope ? SegmentWalk.ReachedEndOfScope : SegmentWalk.Stopped;
    }

    /// <summary>Runs passes until the scope is exhausted, the run ends beneath this attempt, or the attempt is stopped.</summary>
    /// <returns><see langword="true" /> when the run reached the end of its scope and was ended.</returns>
    /// <remarks>
    /// Cancellation is caught rather than raised, because being stopped is how an ordinary segment ends: the executor
    /// cancels the attempt at the execution timeout, at shutdown, and when the job's lease has moved on, the scope's lease
    /// cancels it on the first renewal that does not complete, and none of those says the work failed. What the attempt
    /// owes afterwards is the segment that carries the rest, which is why the caller reaches it on this path as well.
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
    /// <param name="found">The run as this segment found it, whose segment count is the one this segment carried.</param>
    /// <param name="payload">The scope the successor walks.</param>
    /// <param name="runScope">The segment's report, which records what was handed on.</param>
    /// <param name="successorAvailableAt">The instant before which the successor may not be claimed, or <see langword="null" /> for at once.</param>
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
    /// The run moves on only from the segment this one found it on. Two segments of one run can both stop — one walked
    /// while the other found the scope held, or an attempt was repeated after it had already handed on — and each would
    /// otherwise write a successor of its own, which is two chains of segments carrying one walk. The second finds the
    /// count already moved and enqueues the successor the first wrote down instead: that key is answered with the job
    /// already there, and where the first segment stopped between its commit and its enqueue, it is what repairs that.
    /// </para>
    /// <para>
    /// A run that has ended or been replaced by the time this runs reports the segment as having reached the end of the
    /// scope, exactly as the walk's own mid-loop check reports the same race. There is nothing to hand on and nothing
    /// went wrong, and a segment whose span ended with neither signal would be indistinguishable from one that stopped
    /// where nobody wrote down why.
    /// </para>
    /// </remarks>
    private async Task HandOnAsync(
        StoredMailRederivationRun found,
        RederiveStoredMailJobPayload payload,
        IStoredMailRederivationRunScope runScope,
        DateTimeOffset? successorAvailableAt)
    {
        var carrying = await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                if (await this.runStore.FindAsync(found.Scope, attemptCancellationToken) is not { IsOutstanding: true } run
                    || run.RunId != found.RunId)
                {
                    return null;
                }

                if (run.SegmentCount != found.SegmentCount)
                {
                    return run;
                }

                var advanced = run with { SegmentCount = run.SegmentCount + 1 };

                await this.runStore.SaveAsync(session, advanced, attemptCancellationToken);

                return advanced;
            },
            CancellationToken.None);

        if (carrying is null)
        {
            runScope.ReachedEndOfScope();

            return;
        }

        var key = StoredMailRederivationRequests.KeyOf(carrying);
        var successor = successorAvailableAt is { } availableAt
            ? JobEnqueueRequest.CreateAvailableAt(key, payload, found.Scope.Account, availableAt)
            : JobEnqueueRequest.Create(key, payload, found.Scope.Account);

        var enqueued = await this.jobs.EnqueueAsync(successor, CancellationToken.None);

        runScope.HandedOn(enqueued.Outcome is not JobEnqueueOutcome.RefusedAtCapacity);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;

namespace MailFathom.Application.Folders;

/// <summary>Erases the stored mail of a folder an account no longer declares, a bounded pass at a time.</summary>
/// <remarks>
/// It runs the same erasure an operator's own command runs and under the same bound, rather than a second path that
/// disposes of mail: one write that removes stored rows is easier to reason about than two, and the bound is the one
/// the backward pass over stored mail already carries, so one pass is one transaction of a known size however much the
/// folder held.
/// </remarks>
public sealed class WithdrawnMailFolderMailErasureHandler : IJobHandler
{
    private readonly IStoredMailFolderMirrorStore mirrorStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly MailboxSynchronizationOptions options;
    private readonly IJobStore jobs;

    /// <summary>Initializes a new instance of the <see cref="WithdrawnMailFolderMailErasureHandler" /> class.</summary>
    /// <param name="mirrorStore">Erases the stored mail and clears the folder's checkpoint once nothing is left.</param>
    /// <param name="concurrencyRetryPolicy">Commits each pass.</param>
    /// <param name="options">Carries the bound on one pass.</param>
    /// <param name="jobs">Queues the next pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public WithdrawnMailFolderMailErasureHandler(
        IStoredMailFolderMirrorStore mirrorStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        MailboxSynchronizationOptions options,
        IJobStore jobs)
    {
        ArgumentNullException.ThrowIfNull(mirrorStore);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jobs);

        this.mirrorStore = mirrorStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
        this.jobs = jobs;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.EraseWithdrawnMailFolderMail;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    /// <exception cref="JobHandOnRefusedAtCapacityException">Thrown when the queue refused the pass carrying the rest of the erasure.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not EraseWithdrawnMailFolderMailJobPayload named)
        {
            throw new ArgumentException(
                $"A '{JobType.EraseWithdrawnMailFolderMail}' job carries a payload naming an account and a withdrawn folder.",
                nameof(payload));
        }

        var erasure = await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.mirrorStore.EraseFolderMirrorAsync(
                session,
                named.Account,
                named.Folder,
                this.options.MaxReconciledEmailsPerRun,
                attemptCancellationToken),
            cancellationToken);

        if (!erasure.EmailsRemain)
        {
            return;
        }

        var next = named.Next();

        // Outside the attempt's own cancellation, for the reason the held account's erasure hands on outside it: the
        // moment the rest most needs to be written down is the shutdown that stopped this pass.
        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(next.ToIdempotencyKey(), next, named.Account),
            CancellationToken.None);

        if (enqueued.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
        {
            throw new JobHandOnRefusedAtCapacityException(JobType.EraseWithdrawnMailFolderMail);
        }
    }
}

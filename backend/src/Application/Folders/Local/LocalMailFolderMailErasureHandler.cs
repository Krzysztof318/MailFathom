// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;

namespace MailFathom.Application.Folders.Local;

/// <summary>Erases the mail of a held account's erased folders a bounded pass at a time.</summary>
/// <remarks>
/// The bound is the one the backward pass over stored mail and the erasure of an unmirrored folder already carry,
/// rather than a second setting nobody configured, so one pass is one transaction of a known size however much the
/// folders held.
/// </remarks>
public sealed class LocalMailFolderMailErasureHandler : IJobHandler
{
    private readonly ILocalMailFolderStore store;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly MailboxSynchronizationOptions options;
    private readonly IJobStore jobs;

    /// <summary>Initializes a new instance of the <see cref="LocalMailFolderMailErasureHandler" /> class.</summary>
    /// <param name="store">Erases the mail.</param>
    /// <param name="concurrencyRetryPolicy">Commits each pass.</param>
    /// <param name="options">Carries the bound on one pass.</param>
    /// <param name="jobs">Queues the next pass.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public LocalMailFolderMailErasureHandler(
        ILocalMailFolderStore store,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        MailboxSynchronizationOptions options,
        IJobStore jobs)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(jobs);

        this.store = store;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
        this.jobs = jobs;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.EraseLocalMailFolderMail;

    /// <inheritdoc />
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not EraseLocalMailFolderMailJobPayload named)
        {
            throw new ArgumentException(
                $"A '{JobType.EraseLocalMailFolderMail}' job carries a payload naming a held account's erased folder.",
                nameof(payload));
        }

        var erasure = await this.concurrencyRetryPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.store.EraseMailOfErasedFoldersAsync(
                session,
                named.Account,
                this.options.MaxReconciledEmailsPerRun,
                attemptCancellationToken),
            cancellationToken);

        if (!erasure.EmailsRemain)
        {
            return;
        }

        var next = named.Next();

        // Outside the attempt's own cancellation, for the reason a re-derivation hands on outside it: the moment the rest
        // most needs to be written down is the shutdown that stopped this pass.
        var enqueued = await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(next.ToIdempotencyKey(), next, named.Account),
            CancellationToken.None);

        if (enqueued.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
        {
            throw new JobHandOnRefusedAtCapacityException(JobType.EraseLocalMailFolderMail);
        }
    }
}

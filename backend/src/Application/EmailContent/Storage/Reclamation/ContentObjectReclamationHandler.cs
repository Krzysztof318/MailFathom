// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;

namespace MailFathom.Application.EmailContent.Storage.Reclamation;

/// <summary>Carries one segment of a sweep for stored mail nothing points at any more.</summary>
/// <remarks>
/// <para>
/// The queue is what makes the sweep safe to run at all on terms this system already owns. A job is leased, so two
/// instances cannot sweep the same segment at once; an attempt is bounded by the execution timeout and cancelled at
/// shutdown, so a bucket cannot hold a worker; and the concurrency gate is what makes it yield to ordinary work rather
/// than compete with a synchronization run for the same capacity.
/// </para>
/// <para>
/// A segment that stops with objects still ahead of it hands the rest to a segment of its own, exactly as a
/// re-derivation does. That is what keeps a bucket larger than one attempt reachable: without it every occasion would
/// list the same first pages and the tail would never be swept.
/// </para>
/// <para>
/// Running it twice with one payload is the same as running it once. Removing an object nothing holds succeeds, and the
/// decision the run makes about each object is read from the endpoint and the database at the moment it makes it, so a
/// second attempt over one segment removes what the first did not reach and nothing else.
/// </para>
/// <para>
/// A deployment that stores content in the database has no bucket and reclaims nothing. The job type is still known to
/// this build, so a segment enqueued before the endpoint was taken away is answered rather than left for a handler
/// nothing registered.
/// </para>
/// </remarks>
public sealed class ContentObjectReclamationHandler : IJobHandler
{
    private readonly IJobStore jobs;
    private readonly IContentObjectReclamation? reclamation;

    /// <summary>Initializes the handler from the sweep it drives and the queue it hands the rest of one to.</summary>
    /// <param name="jobs">Enqueues the segment that carries whatever this attempt did not reach.</param>
    /// <param name="reclamation">Runs one bounded sweep, or is absent for a deployment storing content in the database.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs" /> is <see langword="null" />.</exception>
    public ContentObjectReclamationHandler(IJobStore jobs, IContentObjectReclamation? reclamation = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        this.jobs = jobs;
        this.reclamation = reclamation;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.ReclaimContentObjects;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not ReclaimContentObjectsJobPayload named)
        {
            throw new ArgumentException(
                $"A '{JobType.ReclaimContentObjects}' job carries a payload naming a place in the object endpoint's listing.",
                nameof(payload));
        }

        if (this.reclamation is null)
        {
            return;
        }

        var run = await this.reclamation.ReclaimAsync(named.ResumeFrom, named.OldestOrphanAge, cancellationToken);

        if (run.ResumeFrom is not { } resumeFrom)
        {
            return;
        }

        var next = named.ContinuingFrom(resumeFrom, run.OldestOrphanAge);

        // Outside the attempt's own cancellation, for the reason a re-derivation hands on outside it: the one moment
        // the rest of a sweep most needs to be written down is the shutdown that stopped it, and an enqueue cancelled
        // by the token that stopped the handler would leave the tail of the bucket unswept until the next occasion.
        await this.jobs.EnqueueAsync(
            JobEnqueueRequest.Create(next.ToIdempotencyKey(), next, accountId: null),
            CancellationToken.None);
    }
}

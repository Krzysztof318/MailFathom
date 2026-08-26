// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One unit of durable background work: what it is, what it points at, and who holds it right now.</summary>
/// <remarks>
/// <para>
/// The columns are what the queue is queried by — the key, the type, the account, the state, and the instant a job
/// becomes available — and the document beside them is what the work is described by. Nothing queries into the
/// document, which is why it is one <c>jsonb</c> column rather than a schema.
/// </para>
/// <para>
/// No optimistic concurrency token sits on this row, deliberately. Every write against a leased job is already
/// conditional on the lease owner still matching, which is a compare-and-set over the fact that actually decides
/// whether the writer still owns the work; a row version beside it would report a conflict for a renewal that changed
/// nothing an attempt cares about.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class JobEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the job type's own name, which is the identity the closed enumeration publishes.</summary>
    /// <remarks>
    /// The column holds the name directly rather than a converted enum, because the name is what the value object is:
    /// it is the word in a log line, the name of a span, and the dimension a counter is broken down by, so the stored
    /// form is the same word an operator has already read everywhere else. A name this build does not declare is left
    /// alone rather than failing a read, which is what makes a rolling deployment safe.
    /// </remarks>
    public required string JobType { get; set; }

    /// <summary>Gets or sets the identity the enqueuer composed, unique with the type across the whole table.</summary>
    public required string IdempotencyKey { get; set; }

    /// <summary>Gets or sets the serialized document describing what the work points at.</summary>
    public required string Payload { get; set; }

    /// <summary>Gets or sets the account the work belongs to, and <see langword="null" /> when it belongs to none.</summary>
    /// <remarks>
    /// Denormalized onto the row for the reason the mutation record denormalizes it: erasure, retention, and any
    /// per-account bound must be a query on an indexed column rather than a search inside the document beside it.
    /// </remarks>
    public string? MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner the work belongs to, and <see langword="null" /> when it belongs to none.</summary>
    /// <remarks>
    /// Nullable exactly as <see cref="MailboxAccountId" /> is, and for the same reason: work no mailbox asked
    /// for belongs to nobody's mail. The two are written together — the enqueue resolves the owner of the account
    /// it names and writes both, or writes neither — so the pair never says an account without saying whose it
    /// is. Denormalizing it is what lets the fair claim rank one owner's waiting work on an index rather than
    /// through a join onto the account table.
    /// </remarks>
    public Guid? OwnerId { get; set; }

    public MailboxAccountEntity? MailboxAccount { get; set; }

    public JobState State { get; set; }

    /// <summary>Gets or sets the instant before which no claim may take the job.</summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>Gets or sets the instant this job's turn comes once its owner's queue is shared with everybody else's.</summary>
    /// <remarks>
    /// <para>
    /// The order a claim takes due work in, and the whole of what makes that order fair. It is a virtual instant rather
    /// than a real one: the enqueue stamps it one spacing past the latest turn the same owner's waiting work already
    /// holds, and never earlier than the job becomes available. An owner with nothing waiting therefore lands on the
    /// instant its work is due, and an owner working through a backlog lands further and further ahead of the clock,
    /// which is what lets somebody else's due job overtake it instead of queueing behind the whole backlog.
    /// </para>
    /// <para>
    /// Never null, so the ordering never falls back to a second column. Never earlier than <see cref="AvailableAt" />
    /// <em>as written</em>: an enqueue floors the turn at the instant the job becomes claimable, and a retry and a
    /// returned dead letter each carry it forward to the instant they name, so no write ever leaves a job holding a
    /// turn it could not take. The two do diverge afterwards, and a release is where: it moves the available instant to
    /// now and leaves the turn where it was, because the attempt gave the work back rather than failing at it, so a
    /// released job resumes the place it already had instead of going to the end of its owner's queue. Nothing may
    /// therefore assume <c>TurnAt &gt;= AvailableAt</c> of a row it reads.
    /// </para>
    /// </remarks>
    public DateTimeOffset TurnAt { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public DateTimeOffset StateChangedAt { get; set; }

    /// <summary>Gets or sets how many attempts have been handed out, counted by the claim rather than by the work.</summary>
    /// <remarks>A process that dies mid-execution never reaches a line that would have counted its attempt, so counting at the claim is what keeps a crash loop visible.</remarks>
    public int AttemptCount { get; set; }

    /// <summary>Gets or sets what the last failed attempt was classified as, and <see langword="null" /> while none has failed.</summary>
    /// <remarks>
    /// Kept beside the reason rather than derived from the state, because a dead letter and a job waiting for its next
    /// attempt both carry one and the state distinguishes only the first of those.
    /// </remarks>
    public JobFailureClassification? LastFailureClassification { get; set; }

    /// <summary>Gets or sets the operator-safe name of what the last attempt failed with, and <see langword="null" /> while none has failed.</summary>
    /// <remarks>
    /// A type name and a stable error code, never an exception message: a handler works on mail, and a library's
    /// message may quote it. This column outlives the run and is read back into every report of the job.
    /// </remarks>
    public string? LastFailureReason { get; set; }

    /// <summary>Gets or sets the W3C <c>traceparent</c> of whatever enqueued the job, and <see langword="null" /> when nothing recorded one.</summary>
    /// <remarks>
    /// The one thing on this row that describes neither the work nor its state. A worker claims a job long after the
    /// span that caused it has ended, so the attempt cannot be that span's child; what this makes possible instead is a
    /// link from the attempt back to the trace, which is a cause hours earlier reached in one step rather than searched
    /// for in logs. Every row written before the column existed carries <see langword="null" />, which is read as an
    /// attempt with nothing to link to.
    /// </remarks>
    public string? EnqueuedTraceParent { get; set; }

    /// <summary>Gets or sets the W3C <c>tracestate</c> that accompanied it, and <see langword="null" /> when there was none.</summary>
    public string? EnqueuedTraceState { get; set; }

    /// <summary>Gets or sets the attempt holding the job, and <see langword="null" /> while none does.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets the instant after which the job is claimable again whatever its holder is doing.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}

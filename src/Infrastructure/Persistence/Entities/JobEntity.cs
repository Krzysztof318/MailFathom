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

    public MailboxAccountEntity? MailboxAccount { get; set; }

    public JobState State { get; set; }

    /// <summary>Gets or sets the instant before which no claim may take the job.</summary>
    public DateTimeOffset AvailableAt { get; set; }

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

    /// <summary>Gets or sets the attempt holding the job, and <see langword="null" /> while none does.</summary>
    public string? LeaseOwner { get; set; }

    /// <summary>Gets or sets the instant after which the job is claimable again whatever its holder is doing.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}

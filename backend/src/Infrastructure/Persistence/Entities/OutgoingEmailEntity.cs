// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class OutgoingEmailEntity
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the account the message is submitted through and sent as.</summary>
    /// <remarks>
    /// It is a plain column rather than a foreign key onto the stored account, deliberately. The account row is created
    /// by the first folder binding synchronization writes, and an account configured to send need never have
    /// synchronized anything — so a key here would refuse a send from a submission-only account instead of recording
    /// it. What the column is for is the same thing the mutation record's copy is for: the outbox query leads with the
    /// account and an index cannot span a join.
    /// </remarks>
    public required string MailboxAccountId { get; set; }

    /// <summary>Gets or sets the owner whose account the message is sent from.</summary>
    public required Guid OwnerId { get; set; }

    public OutgoingEmailOrigin RequesterOrigin { get; set; }

    public required string RequesterIdentity { get; set; }

    /// <summary>Gets or sets the fingerprint of whoever asked for the send, and <see langword="null" /> on a row written before it was kept.</summary>
    /// <remarks>
    /// It is read only for equality, by a caller asking what became of a send it queued or asking for one to be
    /// withdrawn, which is why the fingerprint is stored rather than the identity behind it: the column has a fixed
    /// width whatever an authorization server named a caller, and an outgoing record gains no second identifier for the
    /// person who asked.
    /// </remarks>
    public string? PrincipalFingerprint { get; set; }

    public OutgoingEmailStage Stage { get; set; }

    /// <summary>Gets or sets how many bytes of MIME were stored for this message.</summary>
    /// <remarks>
    /// Kept here as well as on the content row so that the size bound a submission server advertised can be compared
    /// against it, and the outbox listed, without reading the <c>bytea</c> beside it. The two are written in one
    /// transaction and neither is rewritten afterwards, which is what keeps them from drifting.
    /// </remarks>
    public long MimeByteLength { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public DateTimeOffset StageChangedAt { get; set; }

    /// <summary>Gets or sets the instant from which this send may be claimed, which a failed attempt pushes out.</summary>
    /// <remarks>
    /// It starts at the instant the record was written, so a new send is claimable at once. A backoff is written here
    /// rather than derived from the attempt count at claim time, because the delay is drawn with jitter and a claim
    /// that recomputed it would return every send that failed together to the same server in the same instant.
    /// </remarks>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>Gets or sets the instant the author asked the message to leave at, and <see langword="null" /> when they asked for it to leave at once.</summary>
    /// <remarks>
    /// It is a column of its own rather than the available instant read differently, because a failed attempt moves
    /// that one and nothing moves this. Without it a message written for nine in the morning and deferred twice would
    /// have no record of what nine in the morning was, which is the value the lateness bound is measured from.
    /// </remarks>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Gets or sets the zone the due instant was named in, and <see langword="null" /> when the record names no due time.</summary>
    /// <remarks>
    /// Kept beside the instant rather than resolved from it, because an instant alone cannot say which nine in the
    /// morning was meant once the offset changes. Nothing re-derives the instant from it: the resolution happened where
    /// the time was named, and this is what makes the answer readable afterwards.
    /// </remarks>
    public string? DueZoneId { get; set; }

    /// <summary>Gets or sets the attempt currently holding this send, and <see langword="null" /> while none does.</summary>
    /// <remarks>
    /// Every write an attempt makes is conditional on this value still matching it, which is what makes a late writer
    /// whose lease was reclaimed write nothing at all.
    /// </remarks>
    public Guid? LeaseOwner { get; set; }

    /// <summary>Gets or sets when the holding attempt's lease runs out, and <see langword="null" /> while none holds it.</summary>
    /// <remarks>
    /// An expired lease is what makes a send in flight when a process stopped claimable again, without anything having
    /// to be told the process died. It reaches a record at <see cref="OutgoingEmailStage.Recorded" /> only: a send
    /// whose transmission had begun is never handed to a second attempt by an expiry.
    /// </remarks>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Gets or sets the code of the failure the last attempt ended in, and <see langword="null" /> while none has.</summary>
    /// <remarks>
    /// Only the code is kept. A failure message is text assembled at the failure site and may repeat what a remote
    /// server wrote, and this record is read by an operator asking which sends are stuck rather than by anybody
    /// re-reading a log line.
    /// </remarks>
    public int? LastFailureCode { get; set; }

    /// <summary>Gets or sets the reply code the server last answered the transmission with, and <see langword="null" /> while it has answered none.</summary>
    public int? LastReplyCode { get; set; }

    public ICollection<OutgoingEmailRecipientEntity> Recipients { get; } = [];

    /// <summary>Gets the copies of this message MailFathom has put into folders of the mailbox.</summary>
    /// <remarks>
    /// Loaded with the record wherever a caller asks what became of a send, because where the copies are is part of that
    /// answer rather than a second question. At most one row per place, which is the identity that keeps asking to file
    /// the same message twice from producing a second copy in somebody's folder.
    /// </remarks>
    public ICollection<OutgoingEmailFilingEntity> Filings { get; } = [];

    /// <summary>Gets or sets the code of the failure the last filing attempt ended in, and <see langword="null" /> while none has.</summary>
    /// <remarks>
    /// Separate from <see cref="LastFailureCode" /> because the two say different things to whoever reads the record: a
    /// delivery failure means somebody did not receive the message, and this one means the owner cannot see it in their
    /// own mail client. Writing either over the other would lose whichever happened first.
    /// </remarks>
    public int? LastFilingFailureCode { get; set; }

    /// <summary>Gets or sets the stored MIME this record points at, loaded only where a caller asked for it.</summary>
    /// <remarks>
    /// The navigation exists so the payload is erased with the record it belongs to. Nothing that lists or advances a
    /// send loads it, for the reason every raw MIME column carries: a query that materialized it would pull whole
    /// messages into memory to answer a question about their state.
    /// </remarks>
    public OutgoingEmailContentEntity? Content { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    /// <remarks>See the stored-email mapping: this is the system column, not a user-defined one.</remarks>
    public uint ConcurrencyVersion { get; set; }
}

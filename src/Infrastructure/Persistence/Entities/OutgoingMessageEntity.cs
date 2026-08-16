// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class OutgoingMessageEntity
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

    public OutgoingMessageOrigin RequesterOrigin { get; set; }

    public required string RequesterIdentity { get; set; }

    public OutgoingMessageStage Stage { get; set; }

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

    /// <summary>Gets or sets the code of the failure the last attempt ended in, and <see langword="null" /> while none has.</summary>
    /// <remarks>
    /// Only the code is kept. A failure message is text assembled at the failure site and may repeat what a remote
    /// server wrote, and this record is read by an operator asking which sends are stuck rather than by anybody
    /// re-reading a log line.
    /// </remarks>
    public int? LastFailureCode { get; set; }

    /// <summary>Gets or sets the reply code the server last answered the transmission with, and <see langword="null" /> while it has answered none.</summary>
    public int? LastReplyCode { get; set; }

    public ICollection<OutgoingMessageRecipientEntity> Recipients { get; } = [];

    /// <summary>Gets or sets the stored MIME this record points at, loaded only where a caller asked for it.</summary>
    /// <remarks>
    /// The navigation exists so the payload is erased with the record it belongs to. Nothing that lists or advances a
    /// send loads it, for the reason every raw MIME column carries: a query that materialized it would pull whole
    /// messages into memory to answer a question about their state.
    /// </remarks>
    public OutgoingMessageContentEntity? Content { get; set; }

    /// <summary>Gets or sets the PostgreSQL <c>xmin</c> token this row's optimistic concurrency is detected through.</summary>
    /// <remarks>See the stored-email mapping: this is the system column, not a user-defined one.</remarks>
    public uint ConcurrencyVersion { get; set; }
}

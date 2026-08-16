// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;

namespace MailFathom.Domain.Delivery;

/// <summary>Reports what one durable outgoing record holds: the send that was asked for and how far it has got.</summary>
/// <remarks>
/// <para>
/// The record is written before the first SMTP command and advanced as the attempt proceeds, which is what makes a
/// non-atomic submission safe to resume: an attempt reads it and continues from the stage it names rather than starting
/// over. A send is the first thing this system does that leaves the deployment and cannot be undone, so the record
/// exists for one window above all — the one where the message went out and the acknowledgement did not come back —
/// and it makes that window recognizable rather than guessed at afterwards.
/// </para>
/// <para>
/// It also carries the idempotency identity, which is what makes the same authored request arriving twice one delivery
/// rather than two. The identity is enforced by a unique constraint rather than by any code declining to write.
/// </para>
/// <para>
/// It is derived personal data of a kind mail metadata is not: an outgoing record says who this mailbox's owner wrote
/// to and when. It inherits the retention, deletion, and export obligations of the mail beside it, and the MIME it
/// points at is erased with it. Nothing here is mail content — the addresses, the account, the requester, and the
/// reply codes are the envelope and this system's own names for things, and the message itself stays in the content
/// store.
/// </para>
/// </remarks>
public sealed record OutgoingEmailRecord
{
    /// <summary>Gets what everything after the first write refers to this record by, including its stored MIME.</summary>
    public required OutgoingEmailId Id { get; init; }

    /// <summary>Gets the account the message is submitted through and sent as.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the authored act that asked, restored exactly as it was written down.</summary>
    public required OutgoingEmailRequester Requester { get; init; }

    /// <summary>Gets every recipient the message names, with what the server has said about each.</summary>
    public required IReadOnlyList<OutgoingRecipientOutcome> Recipients { get; init; }

    /// <summary>Gets how far along its submission sequence the message has durably reached.</summary>
    public required OutgoingEmailStage Stage { get; init; }

    /// <summary>Gets how many bytes of MIME were stored for this message.</summary>
    /// <remarks>
    /// It is kept beside the record rather than measured from the payload, because both readers of it are answering
    /// without the message in hand: an attempt compares it against the size bound the submission server advertised
    /// before opening anything, and the outbox reports what is queued. Reading the <c>bytea</c> to learn its length
    /// would load every queued message to answer either.
    /// </remarks>
    public required long MimeByteLength { get; init; }

    /// <summary>Gets how many times this send has been attempted, counted before each attempt rather than after it.</summary>
    /// <remarks>
    /// Counting first is what makes the bound survive a crash loop: an attempt that kills the process still counted, so
    /// a message that crashes the host every time reaches a terminal stage instead of being attempted forever.
    /// </remarks>
    public required int AttemptCount { get; init; }

    /// <summary>Gets when the intent was first written down.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>Gets when the record last moved, which is what says how long a stuck send has been stuck.</summary>
    public required DateTimeOffset StageChangedAt { get; init; }

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while no attempt has failed.</summary>
    /// <remarks>
    /// The code is kept and the message is not. A code is a stable identity an operator can look up, while a message is
    /// text assembled at the failure site and may carry what a remote server wrote, so the record holds the first and
    /// never the second.
    /// </remarks>
    public required MailFathomErrorCode? LastFailure { get; init; }

    /// <summary>Gets the reply code the server last answered the message itself with, or <see langword="null" /> while it has answered none.</summary>
    /// <remarks>
    /// It is the answer to the transmission rather than to a recipient, which is a different fact from the reply codes
    /// on <see cref="Recipients" />: a server accepts or refuses each address separately and then answers once for the
    /// body, and a send can be refused at either point.
    /// </remarks>
    public required int? LastReplyCode { get; init; }

    /// <summary>Gets whether the record has reached a stage nothing moves it out of.</summary>
    public bool IsTerminal => this.Stage
        is OutgoingEmailStage.Sent
        or OutgoingEmailStage.Refused
        or OutgoingEmailStage.Cancelled;

    /// <summary>Gets whether the message went out and the server's answer to it never came back.</summary>
    /// <remarks>
    /// A record here is not resumed. Transmitting again would put a second copy in the mailbox of everybody who already
    /// received it, and nothing an outbox can read afterwards says whether the first transmission landed — so the
    /// outcome is established another way or the record is given up on visibly.
    /// </remarks>
    public bool HasUnknownOutcome => this.Stage == OutgoingEmailStage.TransmissionBegun;

    /// <summary>Gets the recipients a later attempt still offers the message to.</summary>
    /// <remarks>
    /// This is what makes a partial acceptance recoverable. A recipient the message reached and one the server
    /// permanently refused are both settled, so an attempt after a partial acceptance offers neither and the people who
    /// already received the message do not receive it twice.
    /// </remarks>
    public IReadOnlyList<OutgoingRecipient> OutstandingRecipients =>
    [
        .. this.Recipients
            .Where(outcome => outcome.IsOutstanding)
            .Select(outcome => outcome.Recipient),
    ];
}

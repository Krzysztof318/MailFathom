// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Scheduling;

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
    public required MailAccountIdentity Account { get; init; }

    /// <summary>Gets the identifier half of <see cref="Account" />, which is what code already narrowed to one owner names.</summary>
    /// <remarks>
    /// Derived rather than stored, so the pair is the one value here and the two halves can never disagree. It is kept
    /// because most readers of this record are inside a scope whose owner is already settled, and naming the identifier
    /// alone there says what the code means.
    /// </remarks>
    public MailAccountId AccountId => this.Account.Id;


    /// <summary>Gets the authored act that asked, restored exactly as it was written down.</summary>
    public required OutgoingEmailRequester Requester { get; init; }

    /// <summary>Gets whoever the send was asked for by, or <see langword="null" /> for a record written before that was kept.</summary>
    /// <remarks>
    /// It is what confines a caller reading a send back, or withdrawing one, to the sends it asked for itself. Absence
    /// is a record from an earlier build rather than a send nobody asked for, and it matches nobody: a caller cannot
    /// prove it queued a record that never said who did, and the operator's own view of the outbox is where such a
    /// record is read from instead.
    /// </remarks>
    public required OutgoingEmailPrincipal? Principal { get; init; }

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

    /// <summary>Gets the instant from which this send may be attempted again.</summary>
    /// <remarks>
    /// It is read rather than only written because it is the one value that says a message is <em>waiting</em> rather
    /// than merely queued. A send whose instant has passed is claimed by the next pass and is gone in seconds; one whose
    /// instant lies ahead sits in the outbox until then, which is the message worth mirroring into a folder the owner
    /// can see.
    /// </remarks>
    public required DateTimeOffset AvailableAt { get; init; }

    /// <summary>Gets the time the send was written to leave at, or <see langword="null" /> when it was written to leave at once.</summary>
    /// <remarks>
    /// <para>
    /// It is the author's own statement of when the message should go, and it is kept beside
    /// <see cref="AvailableAt" /> rather than folded into it because the two answer different questions. The available
    /// instant says when a claim may take the record next, and a failed attempt moves it; this one says what the author
    /// asked for and nothing moves it, which is what makes lateness measurable at all.
    /// </para>
    /// <para>
    /// The zone travels with the instant because a person names a time in a place. A message written on Saturday to
    /// leave at nine on Monday means nine as the clock in that place will read it, daylight saving included, and the
    /// resolution happened once where the time was named rather than being re-derived by whatever reads the record.
    /// </para>
    /// </remarks>
    public required ZonedInstant? DueAt { get; init; }

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

    /// <summary>Gets every copy of this message MailFathom has put into a folder of the mailbox.</summary>
    /// <remarks>
    /// Filing is not part of sending, and the two are kept apart here on purpose: a message is delivered or it is not,
    /// and where its copies are is a separate account of the same message. A send that was delivered and whose copy
    /// could not be filed therefore reads as exactly that — <see cref="OutgoingEmailStage.Sent" /> with no filing and a
    /// reason in <see cref="LastFilingFailure" /> — rather than as a delivery that failed.
    /// </remarks>
    public required IReadOnlyList<OutgoingMailFilingRecord> Filings { get; init; }

    /// <summary>Gets the failure the last filing attempt ended in, or <see langword="null" /> while none has failed.</summary>
    /// <remarks>
    /// It is separate from <see cref="LastFailure" /> because the two answer different questions and an operator acts on
    /// them differently. A delivery failure means somebody did not receive the message; a filing failure means the owner
    /// cannot see it in their own mail client. Overwriting one with the other would lose whichever happened first.
    /// </remarks>
    public required MailFathomErrorCode? LastFilingFailure { get; init; }

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

    /// <summary>Reports whether this send is waiting for an instant that has not arrived yet.</summary>
    /// <param name="asOf">The instant the question is asked at.</param>
    /// <returns><see langword="true" /> when the message is queued and nothing will attempt it yet.</returns>
    /// <remarks>
    /// A send held for seconds and one held until Monday are the same record, and this is what tells them apart. Only a
    /// waiting send is worth mirroring into a folder: a message the next pass will take needs no copy anywhere, and
    /// making one would append and withdraw a message on somebody's mail server for every send.
    /// </remarks>
    public bool IsWaitingAt(DateTimeOffset asOf) =>
        this.Stage == OutgoingEmailStage.Recorded && asOf < this.AvailableAt;

    /// <summary>Reports whether the time this send was written to leave at has passed by further than a deployment allows.</summary>
    /// <param name="asOf">The instant the question is asked at.</param>
    /// <param name="allowedLateness">How late a deployment is willing to deliver a message whose time has passed.</param>
    /// <returns><see langword="true" /> when the message was written for a time long enough ago that it must not be sent unasked.</returns>
    /// <remarks>
    /// <para>
    /// A message is not a housekeeping pass, so a time that came round while nothing was running is not skipped: it was
    /// written to be delivered, and dropping it silently loses correspondence. But a message delivered a week after the
    /// morning it was meant for can be worse than one never sent — it answers a question nobody is still asking, and it
    /// tells its recipient the sender was not paying attention — so the deployment states how late is still timely and
    /// what falls outside that is left for a person to decide about.
    /// </para>
    /// <para>
    /// A send that named no time is never late. Its author asked for it to go as soon as it could, and however long a
    /// retry backoff or an unreachable provider has held it, nothing about when it leaves was ever promised.
    /// </para>
    /// </remarks>
    public bool HasMissedItsDueTime(DateTimeOffset asOf, TimeSpan allowedLateness) =>
        this.DueAt is { } due && asOf - due.Instant > allowedLateness;

    /// <summary>Finds the copy of this message filed into one place, if one was.</summary>
    /// <param name="filing">The place to look for.</param>
    /// <returns>The filing row, or <see langword="null" /> when nothing was filed there.</returns>
    /// <remarks>
    /// One row per place, which is what the durable identity of a filing already is: filing the same message into the
    /// same folder twice would be a second copy, so the answer is a row rather than a list.
    /// </remarks>
    public OutgoingMailFilingRecord? FindFiling(OutgoingMailFiling filing) =>
        this.Filings.FirstOrDefault(candidate => candidate.Filing == filing);

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

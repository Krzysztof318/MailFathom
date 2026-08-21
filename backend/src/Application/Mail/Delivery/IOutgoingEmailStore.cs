// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Keeps the durable record of every message MailFathom has been asked to send.</summary>
/// <remarks>
/// <para>
/// The idempotency identity is the sending account and the authoring act together, and it is enforced by a unique
/// constraint rather than by this contract declining to write. Two callers asking for the same send at the same moment
/// both reach the database, and one of them loses there; a check-then-insert would let both through the window between
/// the two statements, and unlike a duplicated local row a duplicated delivery cannot be withdrawn.
/// </para>
/// <para>
/// Writes take the caller's session, because the record and the MIME it points at are one write: a record whose message
/// was not stored has nothing to transmit, and a message stored under no record is bytes nothing will ever read. Reads
/// take none, because a read joins no transaction.
/// </para>
/// <para>
/// Two writes are the exception to both, and are the two this port hands no session. A claim selects and stamps in one
/// statement so two workers claiming at the same moment take different records rather than the same one, which no
/// session-scoped read followed by a write can promise, and its transaction ends with the claim because the attempt
/// that follows reaches a submission server. The sweep that stamps the records a stopped process left mid-transmission
/// is the other: it is one set-based write over rows nobody holds, and joining a caller's transaction would hold them
/// for as long as that caller ran.
/// </para>
/// <para>
/// Every write an attempt makes afterwards names the lease it holds and is refused when the record has moved on to a
/// later attempt. That is the second half of what keeps one send in one pair of hands: the first is that an attempt is
/// cancelled before its lease can expire underneath it.
/// </para>
/// </remarks>
public interface IOutgoingEmailStore
{
    /// <summary>Writes the intent down, or reads back the record that already holds this idempotency identity.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="principal">Whoever asked for it, as the record will remember them.</param>
    /// <param name="mimeByteLength">How many bytes of MIME are being stored for this message.</param>
    /// <param name="cancellationToken">Cancels the write or the read that precedes it.</param>
    /// <returns>The record for this request, and whether this call is what wrote it down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" />, <paramref name="request" />, or <paramref name="principal" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mimeByteLength" /> is not positive.</exception>
    /// <remarks>
    /// The record starts at <see cref="OutgoingEmailStage.Recorded" /> with every recipient unanswered and no attempt
    /// counted, so opening one sends nothing by itself. An identity that already has a record is answered with that
    /// record unchanged — including its recipients, its recorded length, and the principal the first request was made
    /// under — because the message a retry transmits has to be the one a previous attempt may already have begun
    /// transmitting.
    /// </remarks>
    Task<OpenedOutgoingEmail> OpenAsync(
        IPersistenceSession session,
        OutgoingEmailRequest request,
        OutgoingEmailPrincipal principal,
        long mimeByteLength,
        CancellationToken cancellationToken);

    /// <summary>Reads one record back by the identifier everything after the first write refers to it by.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when none carries that identifier.</returns>
    Task<OutgoingEmailRecord?> FindAsync(OutgoingEmailId outgoingEmailId, CancellationToken cancellationToken);

    /// <summary>Reads the sends of one account that have not reached a terminal stage.</summary>
    /// <param name="accountId">The account whose sends are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding records, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// This is what a restart reads, and the one answer it must not lose is a record left at
    /// <see cref="OutgoingEmailStage.TransmissionBegun" />: that message may or may not have been delivered, and a
    /// process that never looked at it again would leave the question permanently unasked.
    /// </para>
    /// <para>
    /// Oldest first, because the answer starts with whatever has been queued longest. It is bounded like every other
    /// public query, and a caller treats the bound as a page it comes back for rather than as a cut.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Counts how many of the account's sends stand at each stage nothing has finished with.</summary>
    /// <param name="accountId">The account whose sends are counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One count per non-terminal stage, including the stages nothing stands at.</returns>
    /// <remarks>
    /// <para>
    /// It is a count rather than a reading because what it answers is a level: how much this account has waiting, which
    /// is the figure a delivery rate is a rate against. Nothing about any individual send reaches the answer, so it is
    /// publishable as the dimensions of a gauge.
    /// </para>
    /// <para>
    /// A stage nothing stands at is reported as zero rather than left out, so an empty answer means the level was never
    /// measured and not that it is nothing. Whoever publishes it depends on that difference: a drained account has to
    /// report zero, while an account a pass never reached must keep whatever was last known about it rather than have
    /// its backlog cleared by a pass that did nothing.
    /// </para>
    /// <para>
    /// Only the non-terminal stages are counted, which is what keeps it bounded: those are the rows the outstanding
    /// index covers, while everything this deployment has ever sent is history that grows without limit and would make
    /// a per-pass count a scan of it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutboxStageCount>> CountOutstandingByStageAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken);

    /// <summary>Takes up to a batch of the account's due sends and leases each of them to one attempt.</summary>
    /// <param name="request">Whose sends to take, how many, and under what lease.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The records this claim took, oldest first, each with the lease it is held under.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Two things make a record due, and the second is the crash recovery: one at
    /// <see cref="OutgoingEmailStage.Recorded" /> whose next-attempt instant has passed and is held by nobody, and one
    /// whose lease has run out. Nothing has to be told that a process died, because an expired lease is
    /// indistinguishable from one whose holder is gone.
    /// </para>
    /// <para>
    /// A record at <see cref="OutgoingEmailStage.TransmissionBegun" /> is never due, whatever its lease says. Its
    /// message may already be in somebody's mailbox and nothing an outbox can read afterwards says whether it is, so
    /// handing it to a second attempt is the one thing an expiry must not cause.
    /// </para>
    /// <para>
    /// The attempt is counted by the claim rather than by whatever transmits, so a send that kills the process every
    /// time still reaches its bound instead of being attempted forever.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ClaimedOutgoingEmail>> ClaimAsync(
        OutgoingEmailClaimRequest request,
        CancellationToken cancellationToken);

    /// <summary>Marks the account's sends whose transmission was never answered, so an operator reads why they are stuck.</summary>
    /// <param name="accountId">The account whose records are marked.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many records this call marked.</returns>
    /// <remarks>
    /// A record left at <see cref="OutgoingEmailStage.TransmissionBegun" /> by a process that stopped has no attempt
    /// left to record why, so the first pass after the restart writes the code onto it. It marks only a record that
    /// carries no failure yet, which is what keeps it from writing over the reason a live attempt already recorded, and
    /// it moves no stage: the stage is what says the outcome is unknown.
    /// </remarks>
    Task<int> MarkUnknownOutcomesAsync(MailAccountId accountId, CancellationToken cancellationToken);

    /// <summary>Gives a held record back after a failure that can clear, claimable again once the instant named has passed.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record to give back.</param>
    /// <param name="availableAt">The instant from which the record may be claimed again.</param>
    /// <param name="failure">The code identifying what ended the attempt, or <see langword="null" /> when it ended in no failure of its own and the recorded one stands.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is given back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, or when it has reached a terminal stage.</exception>
    /// <remarks>
    /// <para>
    /// It returns the record to <see cref="OutgoingEmailStage.Recorded" />, which is the one place a stage moves
    /// backwards and is why the caller must have established that the message reached nobody it will be offered to
    /// again. An attempt that cannot establish that leaves the record where it is and records the failure instead.
    /// </para>
    /// <para>
    /// A transmission the server acknowledged can end here too, and then no failure is recorded: the addresses it
    /// carried are settled, and what the next attempt offers is the ones the server refused for now.
    /// </para>
    /// </remarks>
    Task DeferAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        DateTimeOffset availableAt,
        MailFathomErrorCode? failure,
        CancellationToken cancellationToken);

    /// <summary>Gives a held record back unfinished, so it is claimable again at once and holds no attempt against it.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record to give back.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the record is given back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, or when it has reached a terminal stage.</exception>
    /// <remarks>
    /// It is the shutdown path and nothing else: a host that stopped before an attempt transmitted anything cost the
    /// send nothing, so the attempt the claim counted is given back with it. Like <see cref="DeferAsync" /> it returns
    /// the record to <see cref="OutgoingEmailStage.Recorded" />, and for the same reason it may only be called by an
    /// attempt that established that nothing was transmitted.
    /// </remarks>
    Task ReleaseAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to <see cref="OutgoingEmailStage.TransmissionBegun" />.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record to advance.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, or when it is not at <see cref="OutgoingEmailStage.Recorded" />.</exception>
    /// <remarks>
    /// It is a transition of its own rather than a value passed to <see cref="AdvanceAsync" />, because it is the one
    /// that has to be durable <em>before</em> the act it describes. Announcing it afterwards would be announcing it
    /// only when the crash it exists for did not happen.
    /// </remarks>
    Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to a terminal stage, recording the reply the server answered the transmission with.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record to advance.</param>
    /// <param name="stage">The terminal stage the send has reached.</param>
    /// <param name="replyCode">The reply code the server answered the message with, or <see langword="null" /> when it answered none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stage" /> is not a terminal stage, or when <paramref name="replyCode" /> is supplied and is not a three-digit reply code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, when it has already reached a terminal stage, or when the stage asked for does not follow the one the record reached.</exception>
    /// <remarks>
    /// <para>
    /// A terminal stage is the only thing this writes, because every non-terminal stage is reached by a transition of
    /// its own.
    /// </para>
    /// <para>
    /// Two of the three follow one stage only, which is the unknown window read from either end.
    /// <see cref="OutgoingEmailStage.Sent" /> follows only <see cref="OutgoingEmailStage.TransmissionBegun" />, so
    /// no record claims a delivery it never recorded a transmission for; <see cref="OutgoingEmailStage.Cancelled" />
    /// follows only <see cref="OutgoingEmailStage.Recorded" />, so no record claims a withdrawal after bytes that may
    /// already have reached somebody. A send stopped mid-transmission therefore reaches none of the three: it stays at
    /// <see cref="OutgoingEmailStage.TransmissionBegun" />, stamped with
    /// <see cref="MailFathomErrorCode.OutgoingEmailOutcomeUnknown" /> by the sweep this store runs before it claims, and
    /// nothing claims it again. Every terminal stage states something about what was received, and that is the one thing
    /// nobody can read there.
    /// </para>
    /// </remarks>
    Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        OutgoingEmailStage stage,
        int? replyCode,
        CancellationToken cancellationToken);

    /// <summary>Records what one attempt settled about the recipients it offered.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record the outcomes belong to.</param>
    /// <param name="outcomes">What the attempt settled, one entry per recipient it offered.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the outcomes are written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" />, <paramref name="lease" />, or <paramref name="outcomes" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, when it has reached a terminal stage, or when an outcome names an address the record does not.</exception>
    /// <remarks>
    /// A recipient the record already settled keeps the answer it has. Nothing offers such a recipient again, so an
    /// outcome about one is an attempt reporting what it was told rather than a fact that can have changed, and taking
    /// it would let a later transient reply undo a delivery that already happened.
    /// </remarks>
    Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken);

    /// <summary>Records the failure the last attempt ended in, without moving the stage.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="lease">The lease the attempt holds the record under.</param>
    /// <param name="outgoingEmailId">The record the attempt belonged to.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the failure is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="lease" /> is <see langword="null" />.</exception>
    /// <exception cref="OutgoingEmailLeaseLostException">Thrown when the record is held by a later attempt.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingEmailId" />, or when it has reached a terminal stage.</exception>
    /// <remarks>The stage stays where the attempt actually got to, which is what a later one reads; the failure says why it got no further.</remarks>
    Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);
}

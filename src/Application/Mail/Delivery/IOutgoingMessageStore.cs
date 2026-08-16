// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// </remarks>
public interface IOutgoingMessageStore
{
    /// <summary>Writes the intent down, or reads back the record that already holds this idempotency identity.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="mimeByteLength">How many bytes of MIME are being stored for this message.</param>
    /// <param name="cancellationToken">Cancels the write or the read that precedes it.</param>
    /// <returns>The record for this request, whether this call created it or an earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mimeByteLength" /> is not positive.</exception>
    /// <remarks>
    /// The record starts at <see cref="OutgoingMessageStage.Recorded" /> with every recipient unanswered and no attempt
    /// counted, so opening one sends nothing by itself. An identity that already has a record is answered with that
    /// record unchanged — including its recipients and its recorded length — because the message a retry transmits has
    /// to be the one a previous attempt may already have begun transmitting.
    /// </remarks>
    Task<OutgoingMessageRecord> OpenAsync(
        IPersistenceSession session,
        OutgoingMessageRequest request,
        long mimeByteLength,
        CancellationToken cancellationToken);

    /// <summary>Reads one record back by the identifier everything after the first write refers to it by.</summary>
    /// <param name="outgoingMessageId">The record to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when none carries that identifier.</returns>
    Task<OutgoingMessageRecord?> FindAsync(OutgoingMessageId outgoingMessageId, CancellationToken cancellationToken);

    /// <summary>Reads the sends of one account that have not reached a terminal stage.</summary>
    /// <param name="accountId">The account whose sends are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding records, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// This is what a restart reads, and the one answer it must not lose is a record left at
    /// <see cref="OutgoingMessageStage.TransmissionBegun" />: that message may or may not have been delivered, and a
    /// process that never looked at it again would leave the question permanently unasked.
    /// </para>
    /// <para>
    /// Oldest first, because the answer starts with whatever has been queued longest. It is bounded like every other
    /// public query, and a caller treats the bound as a page it comes back for rather than as a cut.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutgoingMessageRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Counts one attempt against the record before that attempt is made.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingMessageId">The record to count against.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The number this attempt is, counting from one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingMessageId" />, or when it has reached a terminal stage.</exception>
    Task<int> CountAttemptAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to <see cref="OutgoingMessageStage.TransmissionBegun" />.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingMessageId">The record to advance.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingMessageId" />, or when it is not at <see cref="OutgoingMessageStage.Recorded" />.</exception>
    /// <remarks>
    /// It is a transition of its own rather than a value passed to <see cref="AdvanceAsync" />, because it is the one
    /// that has to be durable <em>before</em> the act it describes. Announcing it afterwards would be announcing it
    /// only when the crash it exists for did not happen.
    /// </remarks>
    Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to a terminal stage, recording the reply the server answered the transmission with.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingMessageId">The record to advance.</param>
    /// <param name="stage">The terminal stage the send has reached.</param>
    /// <param name="replyCode">The reply code the server answered the message with, or <see langword="null" /> when it answered none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="stage" /> is not a terminal stage, or when <paramref name="replyCode" /> is supplied and is not a three-digit reply code.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingMessageId" />, when it has already reached a terminal stage, or when the stage asked for does not follow the one the record reached.</exception>
    /// <remarks>
    /// <para>
    /// A terminal stage is the only thing this writes, because every non-terminal stage is reached by a transition of
    /// its own.
    /// </para>
    /// <para>
    /// Two of the three follow one stage only, which is the unknown window read from either end.
    /// <see cref="OutgoingMessageStage.Sent" /> follows only <see cref="OutgoingMessageStage.TransmissionBegun" />, so
    /// no record claims a delivery it never recorded a transmission for; <see cref="OutgoingMessageStage.Cancelled" />
    /// follows only <see cref="OutgoingMessageStage.Recorded" />, so no record claims a withdrawal after bytes that may
    /// already have reached somebody. A send stopped mid-transmission therefore ends at
    /// <see cref="OutgoingMessageStage.Refused" />, which states that nothing more will be attempted and states nothing
    /// about what was received.
    /// </para>
    /// </remarks>
    Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        OutgoingMessageStage stage,
        int? replyCode,
        CancellationToken cancellationToken);

    /// <summary>Records what one attempt settled about the recipients it offered.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingMessageId">The record the outcomes belong to.</param>
    /// <param name="outcomes">What the attempt settled, one entry per recipient it offered.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the outcomes are written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="outcomes" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingMessageId" />, or when an outcome names an address the record does not.</exception>
    /// <remarks>
    /// A recipient the record already settled keeps the answer it has. Nothing offers such a recipient again, so an
    /// outcome about one is an attempt reporting what it was told rather than a fact that can have changed, and taking
    /// it would let a later transient reply undo a delivery that already happened.
    /// </remarks>
    Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken);

    /// <summary>Records the failure the last attempt ended in, without moving the stage.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="outgoingMessageId">The record the attempt belonged to.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the failure is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="outgoingMessageId" />.</exception>
    /// <remarks>The stage stays where the attempt actually got to, which is what a later one reads; the failure says why it got no further.</remarks>
    Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>Reads an outbox as an operator sees it, and carries out the two decisions they can take about one send.</summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IOutgoingEmailStore" /> because the callers are separate, for the reason the dead-letter
/// store is separate from the job store. That contract is the delivery attempt's, and every write on it names the lease
/// the calling attempt holds; nothing here holds a lease, because an operator is not an attempt. Folding the two
/// together would put an operator's decision behind a lease owner it would have to invent, and would widen the surface a
/// worker sees to include writes no worker may make.
/// </para>
/// <para>
/// Both decisions are conditional on the record still being in the state the decision applies to, and on no live lease
/// holding it. That is what makes them safe to repeat and safe to race: an operator acting on a listing a few minutes
/// old, or two terminals acting together, produce one change and one refusal that says what happened instead — rather
/// than a cancellation that lands while a worker is offering the message to a server.
/// </para>
/// </remarks>
public interface IOutboxOperationStore
{
    /// <summary>Counts what stands at each stage of an outbox.</summary>
    /// <param name="account">The account to count, named by its owner and its identifier, or <see langword="null" /> to count every account this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One count per stage anything stands at; a stage nothing stands at may be absent.</returns>
    /// <remarks>The caller fills in the stages this answer omits, so a store need not enumerate what it counted nothing at.</remarks>
    Task<IReadOnlyList<OutboxStageCount>> CountByStageAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken);

    /// <summary>Serves one bounded page of the sends this deployment has recorded.</summary>
    /// <param name="query">Which sends, and how the page is narrowed and continued.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the boundary the next one is asked with where the reading continues.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The recipients of a send are deliberately not read at all rather than read and dropped, so a page of an outbox
    /// never puts anybody's address into memory on its way to being left out of the answer.
    /// </remarks>
    Task<OutboxPage> ReadPageAsync(OutboxQuery query, CancellationToken cancellationToken);

    /// <summary>Withdraws one send that has not begun transmitting.</summary>
    /// <param name="outgoingEmailId">The send to cancel.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What happened to the send.</returns>
    /// <remarks>
    /// <para>
    /// It moves the record to <see cref="OutgoingEmailStage.Cancelled" /> from
    /// <see cref="OutgoingEmailStage.Recorded" /> and from nowhere else, which is the stage rule
    /// <see cref="IOutgoingEmailStore.AdvanceAsync" /> already states, read from the operator's end: nothing claims a
    /// withdrawal after bytes that may already have reached somebody.
    /// </para>
    /// <para>
    /// A record a live lease holds is refused as well, even at that stage. Such a record is being attempted right now
    /// and may be one statement away from a transmission, so cancelling it would be a race whose losing side is a
    /// message the operator was told had been withdrawn.
    /// </para>
    /// </remarks>
    Task<OutboxDecisionOutcome> CancelAsync(OutgoingEmailId outgoingEmailId, CancellationToken cancellationToken);

    /// <summary>Puts one send back where the next delivery pass claims it, with its attempts given back.</summary>
    /// <param name="outgoingEmailId">The send to offer again.</param>
    /// <param name="refusalRestated">Whether the caller has restated a permanent refusal, which is what a refused send needs before it is offered again.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What happened to the send.</returns>
    /// <remarks>
    /// <para>
    /// Three stages reach it and no others. A send at <see cref="OutgoingEmailStage.TransmissionBegun" /> is the one
    /// this decision exists for — nobody can say what its recipients received, nothing claims it again, and the choice
    /// to risk a second copy is the operator's rather than the code's. A send at
    /// <see cref="OutgoingEmailStage.Recorded" /> is one waiting out a backoff, and offering it again is asking for it
    /// now. A send at <see cref="OutgoingEmailStage.Refused" /> is reached only when
    /// <paramref name="refusalRestated" /> says so, because the refusal on the record is the reason nothing offers it.
    /// </para>
    /// <para>
    /// <see cref="OutgoingEmailStage.Sent" /> and <see cref="OutgoingEmailStage.Cancelled" /> are refused outright. The
    /// first would transmit a message that was already delivered, and the second would undo a withdrawal somebody
    /// decided on.
    /// </para>
    /// <para>
    /// The attempts are given back, because a send that has spent its allowance would otherwise be refused again on its
    /// first attempt and the decision would change nothing. The failure recorded against it is kept, so the record
    /// still says why it stopped until something newer replaces it, and the recipients keep the answers they already
    /// have: an address a server settled is never offered a second time, whoever asked for the send to run again.
    /// </para>
    /// </remarks>
    Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken);
}

/// <summary>States what became of a send an operator decided about.</summary>
/// <remarks>
/// One set for both decisions, because what the refusals say is the same in both: a send this deployment does not hold,
/// a send that has moved past the point the decision applies at, and a send an attempt is holding right now. Which
/// decision was asked for is already the method that was called.
/// </remarks>
public enum OutboxDecisionOutcome
{
    /// <summary>The send was in the state the decision applies to, and the decision was written against it.</summary>
    Accepted = 0,

    /// <summary>No send of this deployment carries the identifier named.</summary>
    RecordUnknown = 1,

    /// <summary>The send has moved past the point at which the decision could be taken.</summary>
    /// <remarks>
    /// For a cancellation it is a send whose transmission has begun or which has already reached a terminal stage; for a
    /// re-queue it is one that was delivered or withdrawn. It is the answer to a second terminal acting on a listing
    /// the first one has already acted on.
    /// </remarks>
    StageDoesNotAllowIt = 2,

    /// <summary>A delivery attempt holds the send right now, so the decision would race it.</summary>
    /// <remarks>Its lease is what frees it: once that has run out the same decision is taken without anything having changed about the message.</remarks>
    AttemptUnderWay = 3,

    /// <summary>The send was permanently refused and the caller did not restate that refusal.</summary>
    /// <remarks>It is a re-queue's refusal alone. What the record says is that a server will not take this message, and offering it again is a decision to disbelieve that rather than a retry.</remarks>
    RefusalNotRestated = 4,
}

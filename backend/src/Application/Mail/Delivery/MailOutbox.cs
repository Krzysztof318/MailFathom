// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Takes a message somebody authored and makes it durable before anything can try to send it.</summary>
/// <remarks>
/// <para>
/// This is the one way into the outbox, and it exists because the two writes beneath it are one decision. A record
/// whose message was never stored describes a send with nothing to transmit; a message stored under no record is bytes
/// nothing will ever read. Both cross the same transaction here, so a crash between them leaves neither rather than
/// half of a send.
/// </para>
/// <para>
/// Being the one way in is also what makes it the place the send grant is asked for a second time. A tool call was
/// already refused at the transport if it could be, and the same question is put here with no transport in the
/// picture, so nothing that reaches the outbox by another route reaches it ungoverned.
/// </para>
/// <para>
/// It is the place this deployment's own bounds on sending are asked for the same reason, and for one more: they are
/// the operator's answer rather than the caller's, so they have to hold for work no caller requested. Whether sending
/// is on for the account at all, who the deployment may write to, and how much may leave in a period are all decided
/// before anything is written down, which is what makes a refusal cost nothing and leave nothing behind.
/// </para>
/// <para>
/// What the message <em>says</em> is asked here for exactly those reasons and answered differently. A deployment that
/// screens outgoing mail refuses a message carrying material it will not let leave, and refuses it whole rather than
/// sending a redacted version of it — every other point this scanner guards publishes something to a reader, where
/// removing what was found still answers the question; here the text is somebody's message, and rewriting it would put
/// words they never chose under their own address.
/// </para>
/// <para>
/// Enqueuing is idempotent by the identity the request carries. The same authored request arriving twice — a rule that
/// ran again, a retried command, a client that resent a call — reads back the record the first one wrote and stores
/// nothing further, so it produces one delivery. What decides that is the unique constraint under the store rather than
/// any check here: two callers arriving together both reach the database, and the loser's retry finds the winner's row.
/// </para>
/// <para>
/// Nothing is sent by this. The record it leaves is at <see cref="OutgoingEmailStage.Recorded" /> with every recipient
/// unanswered, which is the state a delivery attempt reads and continues from.
/// </para>
/// <para>
/// What it does do, once the record is durable, is say so. The signal is what turns an authored act into a send that
/// leaves in seconds rather than at the account's next synchronization run, and it is deliberately the last thing that
/// happens: a signal raised before the commit would point a delivery pass at a record that does not exist yet, and a
/// signal that is refused or lost costs the send the wait until that run rather than the send itself.
/// </para>
/// <para>
/// A send written for a later time says so differently, and this is the whole of what holding one costs. The record is
/// already unclaimable until that instant, so what is missing is somebody to notice the instant arriving — and that is
/// a job on the durable queue, made available at the time the message is due. No timer, no scheduler, and no queue of
/// this feature's own: a message held until Monday rides the same lease, the same capacity bounds, and the same restart
/// behaviour as every other piece of background work, and an instance that was down when the moment came finds the job
/// claimable the second it starts.
/// </para>
/// </remarks>
/// <param name="outgoingEmails">Holds the durable record and its idempotency identity.</param>
/// <param name="contentStore">Holds the composed MIME the record points at.</param>
/// <param name="retryPolicy">Commits both writes together and resolves a lost race for the same identity.</param>
/// <param name="signal">Tells the delivery loop that this account has something to send.</param>
/// <param name="jobs">Carries the moment a held send becomes due, without a timer of this feature's own.</param>
/// <param name="outboxOperations">Writes the withdrawal of a message that has not begun to leave, which is one transition however it was asked for.</param>
/// <param name="authorization">Answers whether whoever reached this is admitted to ask for the send the request states it is.</param>
/// <param name="governor">Answers whether this deployment may send this message at all, whoever is asking.</param>
/// <param name="screening">Answers whether what the message says is something this deployment lets leave.</param>
/// <param name="timeProvider">Says whether a recorded send is due now or is being held for later.</param>
public sealed class MailOutbox(
    IOutgoingEmailStore outgoingEmails,
    IEmailContentStore contentStore,
    OptimisticConcurrencyRetryPolicy retryPolicy,
    MailOutboxSignal signal,
    IJobStore jobs,
    IOutboxOperationStore outboxOperations,
    AccessAuthorization authorization,
    OutgoingMailGovernor governor,
    OutgoingMailScreening screening,
    TimeProvider timeProvider)
{
    /// <summary>The word every held send's dispatch job is keyed by, so one record has one such job however often it is enqueued.</summary>
    private const string HeldSendKeyPrefix = "held-send";

    /// <summary>Writes down a message to be sent, or answers with the record an identical request already left.</summary>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="rawMime">The composed RFC 822 bytes to transmit, stored once and read back for every attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The durable record for this request, and whether this call is what wrote it down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the act the request names as its origin is not what reached this: a caller granted <see cref="MailFathomPermission.MailSend" /> for a command, and MailFathom's own identity for a rule.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when sending is not enabled for the account or the deployment is read-only, when a recipient is one the recipient policy refuses, when the period has reached a ceiling, or when the message carries material this deployment screens outgoing mail for.</exception>
    /// <exception cref="SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the message carries, which refuses the send rather than queueing it unscreened.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write lost its race for the same identity on every allowed attempt.</exception>
    /// <remarks>
    /// The message is not recomposed for a request that already has a record, and the bytes supplied here are then
    /// ignored rather than written over the stored ones. That is what keeps a resumed send one message: a
    /// <c>Message-ID</c> that changed between attempts would thread as a second message in every recipient's client.
    /// </remarks>
    public async Task<OpenedOutgoingEmail> EnqueueAsync(
        OutgoingEmailRequest request,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var principal = this.RequireAdmittedToSend(request.Requester.Origin);

        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing email is recorded with the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        // Asked on every enqueue, including one whose identity already has a record. A request repeated after the
        // deployment was turned read-only, or after its recipient became one the policy refuses, is a request this
        // deployment may no longer act on — and answering it from the record written under the older posture would make
        // an idempotency key a way to carry a permission forward.
        await governor.RequirePermittedAsync(request, cancellationToken);

        // After the governor and before the write, which is the order of cost and the order of authority. A deployment
        // that may not send at all, or may not write to these people, is answered without a scan; only a send that has
        // cleared every bound the operator set is worth spending an analyzer round trip on. And like the governor, this
        // is asked on every enqueue including one whose identity already has a record: a request repeated after the
        // screen was switched on, or after its category list widened, is a request this deployment may no longer act
        // on, and answering it from the record written under the older posture would make an idempotency key a way to
        // carry a message past a policy.
        if (await screening.FindRefusalAsync(rawMime, cancellationToken) is { } screened)
        {
            throw OutgoingMailRefusedException.ContentRefused(screened);
        }

        // Before the unit of work rather than inside it. Under the object backend this reaches the endpoint, and no
        // database transaction may be held open across that — the record below opens one the moment it is written.
        var placedContent = await contentStore.PlaceContentAsync(
            EmailContentKind.OutgoingMessage,
            rawMime,
            cancellationToken);

        var committed = await retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var opened = await outgoingEmails.OpenAsync(
                    session,
                    request,
                    principal,
                    rawMime.Length,
                    attemptCancellationToken);

                await contentStore.SaveOutgoingContentAsync(
                    session,
                    opened.Record.Id,
                    placedContent,
                    attemptCancellationToken);

                return opened;
            },
            cancellationToken);

        if (committed.Record.IsWaitingAt(timeProvider.GetUtcNow()))
        {
            await this.DispatchWhenDueAsync(committed.Record, cancellationToken);

            return committed;
        }

        // A record already delivered by an earlier identical request is signalled all the same. The pass reads the
        // outbox rather than this call, so an account with nothing outstanding costs it one claim that takes nothing —
        // which is cheaper than working out here whether the record this call read back still needs sending.
        signal.Signal(committed.Record.Account);

        return committed;
    }

    /// <summary>Withdraws a message that has not begun to leave, and says what became of the request.</summary>
    /// <param name="outgoingEmailId">The record to withdraw.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the request, which is an answer rather than a failure in every case.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <remarks>
    /// <para>
    /// The grant asked for is the one that lets a caller send, because stopping a message is a decision about somebody
    /// else's mail exactly as sending one is: whoever may write to this mailbox's correspondents is who may decide that
    /// a message they wrote will not go. That is the whole of what separates this from the operator's withdrawal, which
    /// asks the same question of the same record under an administrative grant; the transition itself is written once,
    /// by the statement behind <see cref="IOutboxOperationStore.CancelAsync" />, so neither caller can withdraw a
    /// message the other could not.
    /// </para>
    /// <para>
    /// It withdraws one message and never a declaration behind it. A cancelled occurrence of a recurring send stops
    /// that occurrence, and the next occasion produces a message as it always would — which is why stopping the
    /// declaration is an act of its own with a name of its own, rather than something this call could be asked to mean
    /// as well.
    /// </para>
    /// </remarks>
    public Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return outboxOperations.CancelAsync(outgoingEmailId, cancellationToken);
    }

    /// <summary>Puts the moment a held send becomes due onto the durable queue, so nothing has to watch a clock for it.</summary>
    /// <remarks>
    /// <para>
    /// The key names the record, so the same send has one dispatch however many identical requests reach the outbox,
    /// and a job the queue already holds answers the second one instead of queuing a second moment for one message.
    /// </para>
    /// <para>
    /// A queue that is full refuses the job, and that costs the send its punctuality rather than the send itself: the
    /// record is already durable and already due at the instant it names, so the account's next delivery pass claims it
    /// — the same thing a refused signal costs an ordinary send, and the reason neither refusal is raised to whoever
    /// asked for the message.
    /// </para>
    /// </remarks>
    private Task<JobEnqueueResult> DispatchWhenDueAsync(
        OutgoingEmailRecord record,
        CancellationToken cancellationToken) =>
        jobs.EnqueueAsync(
            JobEnqueueRequest.CreateAvailableAt(
                JobIdempotencyKey.Create(
                    string.Create(CultureInfo.InvariantCulture, $"{HeldSendKeyPrefix}:{record.Id}")),
                HeldSendJobPayload.For(record.Account, record.Id),
                record.Account,
                record.AvailableAt),
            cancellationToken);

    /// <summary>Requires that whatever reached this outbox is the kind of act the request says asked for the send.</summary>
    /// <param name="origin">What the request states asked.</param>
    /// <returns>Whoever asked, as the record will remember them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when it is not.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the origin is one this method was never taught, which is a defect here rather than a refusal.</exception>
    /// <remarks>
    /// <para>
    /// The transport already refused what it could refuse cheaply, and this is the authority: the outbox is the one way
    /// in, so a rule action, a command, a worker, or a protocol added later meets the same question without passing any
    /// middleware. Nothing has been written down when it is asked, which is what makes the refusal cost a caller
    /// nothing but the refusal.
    /// </para>
    /// <para>
    /// The origin is checked rather than trusted, which is what keeps it a fact instead of a label. It is already the
    /// half of the record's identity that says who asked, so admitting each origin under exactly the principal that can
    /// legitimately produce it means neither can be worn by the other: a caller cannot enqueue as a rule and so cannot
    /// borrow a rule's idempotency identity, and work no caller requested cannot enqueue as a command however the grant
    /// is written, because the process identity holds no permission at all.
    /// </para>
    /// <para>
    /// An occurrence of a recurring send is admitted exactly as a rule's message is, and for the same reason: nobody is
    /// present when it is composed. What the owner authorized was the declaration, at a boundary that asked them for
    /// the grant then, and the occasion that follows is this process acting on what they wrote down.
    /// </para>
    /// <para>
    /// It is also where the principal is established, rather than anywhere a request is built. Nobody states who asked
    /// for a send: the record remembers what this deployment admitted, which is what makes reading a send back and
    /// withdrawing one confined to whoever queued it instead of to whoever claims to have.
    /// </para>
    /// </remarks>
    private OutgoingEmailPrincipal RequireAdmittedToSend(OutgoingEmailOrigin origin)
    {
        switch (origin)
        {
            case OutgoingEmailOrigin.Command:
                authorization.RequirePermission(MailFathomPermission.MailSend);

                break;

            case OutgoingEmailOrigin.Rule:
            case OutgoingEmailOrigin.Schedule:
                authorization.RequireProcessIdentity();

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(origin),
                    origin,
                    "The outgoing email origin names no act this outbox admits.");
        }

        // Whichever branch admitted the send established that a principal is present, so an absent identity here would
        // be a principal source contradicting the check that just passed rather than an unauthenticated caller.
        return OutgoingEmailPrincipal.Of(authorization.PrincipalIdentity
            ?? throw new InvalidOperationException(
                "An admitted send reached the outbox under no identity, so nothing could be recorded as having asked for it."));
    }
}

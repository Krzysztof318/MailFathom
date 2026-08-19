// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
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
/// </remarks>
/// <param name="outgoingEmails">Holds the durable record and its idempotency identity.</param>
/// <param name="contentStore">Holds the composed MIME the record points at.</param>
/// <param name="retryPolicy">Commits both writes together and resolves a lost race for the same identity.</param>
/// <param name="signal">Tells the delivery loop that this account has something to send.</param>
/// <param name="authorization">Answers whether whoever reached this is admitted to ask for the send the request states it is.</param>
/// <param name="governor">Answers whether this deployment may send this message at all, whoever is asking.</param>
public sealed class MailOutbox(
    IOutgoingEmailStore outgoingEmails,
    IEmailContentStore contentStore,
    OptimisticConcurrencyRetryPolicy retryPolicy,
    MailOutboxSignal signal,
    AccessAuthorization authorization,
    OutgoingMailGovernor governor)
{
    /// <summary>Writes down a message to be sent, or answers with the record an identical request already left.</summary>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="rawMime">The composed RFC 822 bytes to transmit, stored once and read back for every attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The durable record for this request, whether this call created it or an earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the act the request names as its origin is not what reached this: a caller granted <see cref="MailFathomPermission.MailSend" /> for a command, and MailFathom's own identity for a rule.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when sending is not enabled for the account or the deployment is read-only, when a recipient is one the recipient policy refuses, or when the period has reached a ceiling.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write lost its race for the same identity on every allowed attempt.</exception>
    /// <remarks>
    /// The message is not recomposed for a request that already has a record, and the bytes supplied here are then
    /// ignored rather than written over the stored ones. That is what keeps a resumed send one message: a
    /// <c>Message-ID</c> that changed between attempts would thread as a second message in every recipient's client.
    /// </remarks>
    public async Task<OutgoingEmailRecord> EnqueueAsync(
        OutgoingEmailRequest request,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.RequireAdmittedToSend(request.Requester.Origin);

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

        var record = await retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var opened = await outgoingEmails.OpenAsync(
                    session,
                    request,
                    rawMime.Length,
                    attemptCancellationToken);

                await contentStore.SaveOutgoingContentAsync(
                    session,
                    opened.Id,
                    rawMime,
                    attemptCancellationToken);

                return opened;
            },
            cancellationToken);

        // A record already delivered by an earlier identical request is signalled all the same. The pass reads the
        // outbox rather than this call, so an account with nothing outstanding costs it one claim that takes nothing —
        // which is cheaper than working out here whether the record this call read back still needs sending.
        signal.Signal(record.AccountId);

        return record;
    }

    /// <summary>Requires that whatever reached this outbox is the kind of act the request says asked for the send.</summary>
    /// <param name="origin">What the request states asked.</param>
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
    /// </remarks>
    private void RequireAdmittedToSend(OutgoingEmailOrigin origin)
    {
        switch (origin)
        {
            case OutgoingEmailOrigin.Command:
                authorization.RequirePermission(MailFathomPermission.MailSend);

                break;

            case OutgoingEmailOrigin.Rule:
                authorization.RequireProcessIdentity();

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(origin),
                    origin,
                    "The outgoing email origin names no act this outbox admits.");
        }
    }
}

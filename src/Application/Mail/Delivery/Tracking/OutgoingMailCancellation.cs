// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Withdraws a send the caller queued, while there is still a window in which withdrawing one means anything.</summary>
/// <remarks>
/// <para>
/// This is the only point at which sending is reversible at all. Between the record being written and the first byte of
/// the body going out there is a window — seconds ordinarily, longer where an operator configured a hold — and a caller
/// that has just realized it queued the wrong thing can close it. Past that the message is somebody else's and the
/// answer says so plainly rather than reporting a withdrawal that did not happen.
/// </para>
/// <para>
/// <b>The withdrawal itself is one statement and this is a second authorization over it rather than a second
/// implementation of it.</b> <see cref="IOutboxOperationStore.CancelAsync" /> decides and writes in one statement,
/// conditioned on the same facts a claim is conditioned on, so a send an attempt is holding is left alone instead of
/// being cancelled out from under a session that may be part-way through an envelope. A second statement written here
/// would be two accounts of one invariant, and whichever drifted would be found as a message somebody was told had been
/// withdrawn. What differs between the two callers is who may ask and about which records: an operator holds
/// <see cref="MailFathomPermission.AdminOperate" /> and may name any send this deployment holds, while a caller here
/// holds <see cref="MailFathomPermission.MailSend" /> and reaches only what it queued.
/// </para>
/// <para>
/// Withdrawing a send twice is one withdrawal. A record already at <see cref="OutgoingEmailStage.Cancelled" /> is
/// answered with itself and nothing is written, which is what makes the tool over it honestly idempotent rather than
/// idempotent as long as nobody repeats a call.
/// </para>
/// </remarks>
/// <param name="reader">Decides which send this caller may act on at all, in the one place that is decided.</param>
/// <param name="outbox">Performs the conditional withdrawal, and reports what stopped it where it wrote nothing.</param>
/// <param name="authorization">Answers whether the caller holds the grant that lets it send.</param>
public sealed class OutgoingMailCancellation(
    OutgoingMailReader reader,
    IOutboxOperationStore outbox,
    AccessAuthorization authorization)
{
    /// <summary>Withdraws the send one identifier names, where nothing has begun transmitting it.</summary>
    /// <param name="outgoingEmailId">The record to withdraw.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>The record as it stands after the call, which for a withdrawal is at <see cref="OutgoingEmailStage.Cancelled" />.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was not reached by a caller granted <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <exception cref="QueuedSendRefusedException">Thrown when no send this caller queued is held under that identifier, or when the send can no longer be withdrawn.</exception>
    /// <remarks>
    /// The grant is the sending one, because withdrawing a send is part of sending rather than a separate power: what a
    /// caller may stop is exactly what it was allowed to start, and nothing here reaches a message it did not queue.
    /// The scoping runs before the withdrawal rather than beside it, so a record this caller may not be told about is
    /// never named to a store that acts on whatever identifier it is handed.
    /// </remarks>
    public async Task<OutgoingEmailRecord> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var record = await reader.ReadAsync(outgoingEmailId, cancellationToken);

        if (record.Stage == OutgoingEmailStage.Cancelled)
        {
            return record;
        }

        // The stage is read before the statement only to spare a write that could never match: a record past
        // OutgoingEmailStage.Recorded is one nothing withdraws, whatever happens next. What decides is still the
        // statement, because a delivery pass may take the record between this reading and it.
        if (record.Stage != OutgoingEmailStage.Recorded)
        {
            throw QueuedSendRefusedException.NoLongerCancellable();
        }

        var outcome = await outbox.CancelAsync(outgoingEmailId, cancellationToken);

        // The stage read above is what the record was, and a delivery pass may have taken it since. Whether it could
        // still be withdrawn is therefore the statement's answer rather than that reading, and the record is read once
        // more either way: to report the withdrawal that happened, or to find that a second call had already made it.
        var current = await reader.ReadAsync(outgoingEmailId, cancellationToken);

        return outcome == OutboxDecisionOutcome.Accepted || current.Stage == OutgoingEmailStage.Cancelled
            ? current
            : throw QueuedSendRefusedException.NoLongerCancellable();
    }
}

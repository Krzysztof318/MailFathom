// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Reads back one send the caller asked for, so an asynchronous result becomes an answerable question.</summary>
/// <remarks>
/// <para>
/// Queueing is what a send is, so something has to say what became of what was queued. Without this a caller holds an
/// identifier and no way to learn whether the message left, and the worst thing available to an agent in that position
/// is to send again — which is why this exists beside the sending tools rather than as a convenience beyond them.
/// </para>
/// <para>
/// <b>It reads one record and never a set.</b> There is no listing here and none is coming: a call names the record it
/// already holds an identifier for, so the answer is bounded at one and nothing on this path can be walked into an
/// export of what a mailbox has sent.
/// </para>
/// <para>
/// What a caller may read is what that caller queued. The record remembers the principal the outbox admitted it under,
/// and a record admitted under any other answers exactly as a record that does not exist — including a record a rule
/// queued, which no caller asked for at all and which the origin check refuses on its own even where a credential
/// happens to be named as this process is.
/// </para>
/// </remarks>
/// <param name="outgoingEmails">Holds the durable record of every send.</param>
/// <param name="authorization">Answers whether the caller holds the grant that lets it send, and says who it is.</param>
public sealed class OutgoingMailReader(
    IOutgoingEmailStore outgoingEmails,
    AccessAuthorization authorization)
{
    /// <summary>Reads back the send one identifier names, for the caller that asked for it.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The durable record, exactly as it stands.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was not reached by a caller granted <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <exception cref="QueuedSendRefusedException">Thrown when no send this caller queued is held under that identifier.</exception>
    /// <remarks>
    /// The grant is the sending one rather than the reading one. What this answers is what the caller itself asked to
    /// have sent, so a credential granted to read a mailbox learns nothing here — and a tool that answered under the
    /// read grant would be a way to see outgoing correspondence without ever being allowed to produce any.
    /// </remarks>
    public async Task<OutgoingEmailRecord> ReadAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var record = await outgoingEmails.FindAsync(outgoingEmailId, cancellationToken);

        return record is not null && this.WasQueuedByTheCaller(record)
            ? record
            : throw QueuedSendRefusedException.NotFound();
    }

    /// <summary>Reports whether the record in hand is one this caller asked for.</summary>
    /// <remarks>
    /// Both halves are required. The origin keeps a send this deployment made for itself out of every caller's reach
    /// whatever a credential is named, and the principal keeps one caller's sends out of another's; either alone would
    /// leave a way for a record to be read by somebody who did not ask for it.
    /// </remarks>
    private bool WasQueuedByTheCaller(OutgoingEmailRecord record) =>
        record.Requester.Origin == OutgoingEmailOrigin.Command
        && record.Principal is { } queuedBy
        && authorization.PrincipalIdentity is { } identity
        && queuedBy == OutgoingEmailPrincipal.Of(identity);
}

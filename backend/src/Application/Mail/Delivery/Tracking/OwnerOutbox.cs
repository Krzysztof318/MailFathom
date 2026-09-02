// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Serves one owner their own outbox: what is queued, what became of one send, and the two decisions about it.</summary>
/// <remarks>
/// <para>
/// It is the owner's counterpart of the administrative <see cref="OutboxOperations" /> and of the per-caller
/// <see cref="OutgoingMailReader" />, and the three differ in exactly one thing: whose sends they reach. An operator
/// reaches every send this deployment holds, a tool caller reaches the sends that caller itself queued, and a person
/// reaches the sends their own mailboxes are sending — which is what a mail client shows, and what makes a send queued
/// from one head answerable from another.
/// </para>
/// <para>
/// <b>A listing names an account and never the deployment.</b> The narrowing is resolved against the accounts the
/// caller's owner owns, so there is no unnarrowed reading here at all: one that fell back to every account would page
/// through every owner's outgoing mail, which is the deployment-wide catalog an owner-facing surface must never
/// compose. An account another owner owns is refused exactly as one nobody configured.
/// </para>
/// <para>
/// What the answers may carry is what the administrative reading already settled and for the same reasons. A page
/// names no recipient and no subject, because a page of an outbox would otherwise be an export of who this owner
/// writes to; one send read by identity names its recipients and what each was told, because that is the question it
/// was asked. Neither reads the message.
/// </para>
/// <para>
/// Every act here is <see cref="MailFathomPermission.MailSend" />, including the readings. What an outbox says is what
/// this owner is sending, so a credential granted to read a mailbox learns nothing here — and withdrawing a send is
/// part of sending rather than a power beside it, which is the same reading the per-caller cancellation takes.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which accounts the caller's owner owns, and therefore which one a listing may name.</param>
/// <param name="outgoingEmails">Holds the durable record of every send.</param>
/// <param name="outbox">Performs the two conditional decisions, in the one place each is decided.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it send.</param>
public sealed class OwnerOutbox(
    ICallerMailAccountCatalog accountCatalog,
    IOutgoingEmailStore outgoingEmails,
    IOutboxOperationStore outbox,
    AccessAuthorization authorization)
{
    /// <summary>Reads one page of what the named account of this owner is sending, newest first.</summary>
    /// <param name="account">The account to read, which is required rather than optional.</param>
    /// <param name="stage">The stage to narrow to, or <see langword="null" /> for every stage.</param>
    /// <param name="pageSize">How many sends the page may hold, or <see langword="null" /> for the default.</param>
    /// <param name="cursor">The cursor the previous page returned, or <see langword="null" /> for the first page.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, or the reason the request named none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when <paramref name="account" /> names an account the caller's owner does not own.</exception>
    public async Task<OwnerOutboxPageResult> ReadPageAsync(
        MailAccountSelector account,
        OutgoingEmailStage? stage,
        int? pageSize,
        OutboxCursor? cursor,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var owned = accountCatalog.OwnedAccounts.FirstOrDefault(candidate => candidate.IsNamedBy(account))
            ?? throw new MailAccountNotAccessibleException(account);

        var queryResult = OutboxQuery.Create(owned.Identity, stage, pageSize, cursor);

        if (queryResult.Query is not { } query)
        {
            return new OwnerOutboxPageResult(null, queryResult.Outcome);
        }

        var page = await outbox.ReadPageAsync(query, cancellationToken);

        return new OwnerOutboxPageResult(page, OutboxQueryOutcome.Accepted);
    }

    /// <summary>Reads back one send of this owner's, with what each of its recipients was told.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this owner has no send under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <remarks>
    /// A send of another owner's answers exactly as one nobody holds, and so does a send this owner's account made
    /// that a rule asked for: the origin is not part of the scoping here, because the mail leaves this owner's own
    /// mailbox whoever asked for it, and a person watching their outbox is entitled to see what is going out of it.
    /// </remarks>
    public async Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        var record = await outgoingEmails.FindAsync(outgoingEmailId, cancellationToken);

        return record is not null && record.Account.Owner == accountCatalog.Owner ? record : null;
    }

    /// <summary>Withdraws one of this owner's sends, where nothing has begun transmitting it.</summary>
    /// <param name="outgoingEmailId">The record to withdraw.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, which is <see cref="OutboxDecisionOutcome.RecordUnknown" /> for one this owner does not hold.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <remarks>
    /// The scoping runs before the withdrawal rather than beside it, so a record this owner may not be told about is
    /// never named to a store that acts on whatever identifier it is handed. What decides whether the send could still
    /// be withdrawn is the store's own conditional statement, which is what keeps a delivery pass from having a message
    /// cancelled out from under it mid-envelope.
    /// </remarks>
    public async Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return await this.FindAsync(outgoingEmailId, cancellationToken) is null
            ? OutboxDecisionOutcome.RecordUnknown
            : await outbox.CancelAsync(outgoingEmailId, cancellationToken);
    }

    /// <summary>Offers one of this owner's sends again, which is the decision this system will not take on its own.</summary>
    /// <param name="outgoingEmailId">The record to offer again.</param>
    /// <param name="refusalRestated">Whether the caller has restated a permanent refusal, which a refused send needs before it is offered again.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>What became of the send, which is <see cref="OutboxDecisionOutcome.RecordUnknown" /> for one this owner does not hold.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <remarks>
    /// It names one send and never a set, for the reason the administrative decision does: a message whose outcome
    /// nobody knows may already be in somebody's mailbox, so offering it again is a decision about that one message
    /// and a filtered re-queue would be an unknown number of duplicates asked for in one request.
    /// </remarks>
    public async Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        return await this.FindAsync(outgoingEmailId, cancellationToken) is null
            ? OutboxDecisionOutcome.RecordUnknown
            : await outbox.RequeueAsync(outgoingEmailId, refusalRestated, cancellationToken);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Operations;

/// <summary>What an operator does about an outbox: read what stands in it, and decide about one send that is stuck.</summary>
/// <remarks>
/// <para>
/// The stores keep the records and are written by the delivery pass; this is what an operator reaches, and it exists so
/// that the grant is asked where the decision is made rather than only at the routes serving it today. Reading reports
/// the deployment's own state, while cancelling a send and offering one again both change what leaves the deployment,
/// so the two are published under different grants and a credential provisioned to watch an outbox cannot act on it.
/// </para>
/// <para>
/// Neither decision sends anything. A re-queue writes the record back to the stage the next pass claims from, and a
/// cancellation writes a terminal stage nothing claims, which is why both answer immediately whatever the message was
/// for and whichever worker eventually picks it up.
/// </para>
/// <para>
/// The two readings differ in what they may say about people, and deliberately so. A page names no recipient, because a
/// listing of an outbox is a listing of who this owner writes to; one send read by its own identifier names its
/// recipients and what each of them was told, because that is the question that was asked and it cannot be answered
/// without them. Neither reads the message: no subject, no body, and no raw MIME reaches this surface at all.
/// </para>
/// </remarks>
public sealed class OutboxOperations
{
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly IOutboxOperationStore outbox;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the operations over a deployment's outbox.</summary>
    /// <param name="outgoingEmails">Reads one send back by the identifier every decision names it by.</param>
    /// <param name="outbox">Counts and pages the outbox, and performs a decision about one send.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OutboxOperations(
        IOutgoingEmailStore outgoingEmails,
        IOutboxOperationStore outbox,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(authorization);

        this.outgoingEmails = outgoingEmails;
        this.outbox = outbox;
        this.authorization = authorization;
    }

    /// <summary>Reports how much stands at each stage of an outbox.</summary>
    /// <param name="account">The account to report on, named by its owner and its identifier, or <see langword="null" /> for every account this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summary, with one count per declared stage.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<OutboxSummary> ReadSummaryAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return OutboxSummary.Of(await this.outbox.CountByStageAsync(account, cancellationToken));
    }

    /// <summary>Reads one page of the sends this deployment has recorded.</summary>
    /// <param name="query">The filters and where the page continues from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, and the cursor the following one is asked with.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public Task<OutboxPage> ReadPageAsync(OutboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.outbox.ReadPageAsync(query, cancellationToken);
    }

    /// <summary>Reads one send by the identifier every decision names it by, with what each recipient was told.</summary>
    /// <param name="outgoingEmailId">The send to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The record, or <see langword="null" /> when this deployment holds no send with that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminAuditRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// This is the one reading that names addresses, and it is bounded to one record for that reason: a caller that
    /// asks about a specific send by identity is asking about a send they already have in front of them, while a
    /// listing that carried the same fields would be an export of somebody's correspondence a page at a time. It is
    /// also why the grant is the one every other reading of identified third parties is published under rather than
    /// the one its two neighbours share.
    /// </remarks>
    public Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminAuditRead);

        return this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken);
    }

    /// <summary>Withdraws one send that has not begun transmitting.</summary>
    /// <param name="outgoingEmailId">The send to cancel.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the send, including that it was not one this deployment holds or had already moved on.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>It is the only point at which sending is reversible at all, which is exactly why it refuses everything past that point rather than trying to catch up with it.</remarks>
    public Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.outbox.CancelAsync(outgoingEmailId, cancellationToken);
    }

    /// <summary>Offers one send again, which is the decision the system deliberately will not take on its own.</summary>
    /// <param name="outgoingEmailId">The send to offer again.</param>
    /// <param name="refusalRestated">Whether the caller has restated a permanent refusal, which a refused send needs before it is offered again.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What became of the send, including that it was not one this deployment holds or had already moved on.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminOperate" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// A send whose transmission was never answered may already be in somebody's mailbox, and offering it again may put
    /// a second copy there. Nothing in this system will decide that, which is why the record stays where it is until an
    /// operator says so — and why this is an act on one named send rather than anything that can be asked for in bulk.
    /// </remarks>
    public Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminOperate);

        return this.outbox.RequeueAsync(outgoingEmailId, refusalRestated, cancellationToken);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Turns one draft into an ordinary send, carrying the bytes the draft already is.</summary>
/// <remarks>
/// <para>
/// A promoted draft is not a second kind of send. What comes out is the outgoing record every other send is written
/// down as, transmitted through the same outbox and filed by the same mechanism, and what goes in is the stored MIME
/// the drafts folder already shows — so the message the owner read in their own mail client is byte for byte the
/// message their correspondent receives. Nothing is recomposed, which is what keeps the <c>Message-ID</c> the one the
/// draft has been carrying.
/// </para>
/// <para>
/// <b>Every bound this deployment sets is asked again here, not only when the draft was written.</b> A draft may have
/// been composed months before the recipient policy was tightened, the ceilings were lowered, or sending was turned off
/// for the account — so the answer that matters is the one that holds at the moment the message would leave. The
/// governor is asked by the outbox, where every send passes, and the size the deployment composes is compared against
/// the bytes actually stored rather than against what was measured when they were composed.
/// </para>
/// <para>
/// <b>A promotion that fails leaves the draft exactly as it was.</b> Nothing about the draft is written until the
/// outgoing record exists, so a refused recipient, a full period, an account that has since been turned read-only, and
/// a message too large all leave the owner the draft they wrote.
/// </para>
/// <para>
/// The draft is not deleted here either. The record is queued rather than sent, so the copy in the drafts folder stands
/// until the message has actually been delivered, and the pass that settles the send is what gives the draft up.
/// </para>
/// </remarks>
public sealed class MailDraftPromotion
{
    private readonly IMailDraftStore drafts;
    private readonly IEmailContentStore contentStore;
    private readonly MailOutbox outbox;
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly OutgoingEmailBounds bounds;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the promotion from the draft it reads and the outbox it writes the send into.</summary>
    /// <param name="drafts">Holds the draft and the record of what it was promoted to.</param>
    /// <param name="contentStore">Holds the stored MIME the send carries unchanged.</param>
    /// <param name="outbox">Writes the send down, and is where this deployment's bounds are asked again.</param>
    /// <param name="outgoingEmails">Reads back the record a draft was already promoted to.</param>
    /// <param name="retryPolicy">Commits the mark that names the send on the draft.</param>
    /// <param name="bounds">States how large a message this deployment sends.</param>
    /// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it send.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailDraftPromotion(
        IMailDraftStore drafts,
        IEmailContentStore contentStore,
        MailOutbox outbox,
        IOutgoingEmailStore outgoingEmails,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        OutgoingEmailBounds bounds,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(authorization);

        this.drafts = drafts;
        this.contentStore = contentStore;
        this.outbox = outbox;
        this.outgoingEmails = outgoingEmails;
        this.retryPolicy = retryPolicy;
        this.bounds = bounds;
        this.authorization = authorization;
    }

    /// <summary>Queues one draft for delivery, or refuses it naming what stopped it.</summary>
    /// <param name="draftId">The draft to send.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>The durable record the message was written down as.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when no draft is held under that identifier, when the draft names nobody to send it to, or when the stored message exceeds what this deployment sends.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when sending is not enabled for the account, a recipient is one the recipient policy refuses, or the period has reached a ceiling.</exception>
    /// <remarks>
    /// <para>
    /// A draft that has already been promoted answers with the record its promotion wrote rather than writing a second
    /// one, so a caller that asked again queues one message. That read is what answers a caller whose first answer
    /// never reached it, and it settles nothing about two callers arriving together: both would find the draft
    /// unpromoted, because a read cannot see a write that has not happened yet.
    /// </para>
    /// <para>
    /// What settles that is the request's own identity, which is why this takes no key from whoever asked. A draft is
    /// promoted once, so the draft is the act — two callers promoting one draft compose one identity, the outbox
    /// answers the second with the record the first opened, and the two of them then write that same record onto the
    /// draft. A key somebody supplied would make the two asks two requests and put the message in the recipient's
    /// mailbox twice, which nothing downstream can withdraw.
    /// </para>
    /// </remarks>
    public async Task<OutgoingEmailRecord> PromoteAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailSend);

        if (await this.drafts.FindAsync(draftId, cancellationToken) is not { IsDiscarded: false } draft)
        {
            throw MailDraftRefusedException.NotFound();
        }

        if (draft.PromotedTo is { } alreadyPromoted)
        {
            return await this.outgoingEmails.FindAsync(alreadyPromoted, cancellationToken)
                ?? throw MailDraftRefusedException.NotFound();
        }

        if (!draft.IsAddressed)
        {
            throw MailDraftRefusedException.NotAddressed();
        }

        if (draft.MimeByteLength > this.bounds.MaxMessageBytes)
        {
            // Asked against the stored length rather than against what the composition measured, because the bound is
            // the operator's and may have been lowered since the draft was written. Reading the payload to learn its
            // size would load a message to answer a question the record already carries.
            throw MailDraftRefusedException.From(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.Message,
                this.bounds.MaxMessageBytes));
        }

        var content = await this.contentStore.FindMailDraftContentAsync(draftId, cancellationToken);

        if (content is null || content.RawMime.IsEmpty)
        {
            // The record and its message are written in one transaction, so a draft without one describes a message
            // that can never be sent rather than one still being stored.
            throw MailDraftRefusedException.NotFound();
        }

        var request = OutgoingEmailRequest.Create(
            draft.AccountId,
            OutgoingEmailRequester.Draft(draftId),
            draft.Recipients);

        // Everything this deployment refuses a send for is asked inside this call, with no boundary in the picture, so
        // a draft written before a policy tightened cannot be sent past it. Nothing about the draft has been written
        // yet, which is what leaves it intact when the answer is no.
        var record = await this.outbox.EnqueueAsync(request, content.RawMime, cancellationToken);

        await this.retryPolicy.CommitAsync(
            (session, token) => this.drafts.RecordPromotedAsync(session, draftId, record.Id, token),
            cancellationToken);

        return record;
    }
}

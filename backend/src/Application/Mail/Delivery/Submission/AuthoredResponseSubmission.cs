// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Submission;

/// <summary>Takes an answer somebody wrote to a stored email and queues it, which is the whole of what asking to reply or forward does.</summary>
/// <remarks>
/// <para>
/// It is the counterpart of <see cref="AuthoredMailSubmission" /> for the two sends that begin from mail this
/// deployment already holds, and it composes the same three steps in the same order: the answer is authored from the
/// stored copy, the authored message becomes MIME, and the MIME and the request become a durable record. What replaces
/// the resolution of an account and a recipient list is the authoring itself, because a reply is addressed by the
/// message it answers rather than by whoever asked for it.
/// </para>
/// <para>
/// <b>Nothing here transmits, and nothing here composes.</b> The threading identifiers, the quoted original, the
/// carried attachments, and the subject prefix are <see cref="StoredEmailResponseAuthoring" />'s, and this use case
/// neither reads them nor could: what it receives back is an ordinary authored message, indistinguishable at this
/// point from one that answers nothing. That is what keeps a reply and a forward bounded by the same numbers and
/// composed by the same code as every other send.
/// </para>
/// <para>
/// Two grants are asked for, and they are asked for in this order. Sending is refused first, because a caller that may
/// not send has no business reaching a use case that reads the mail it would have quoted; reading is refused by the
/// authoring beneath, because an answer quotes the message it answers and a forward carries that message's files, so
/// anything that reached here without it would read mail by asking to reply to it.
/// </para>
/// <para>
/// The message is composed against
/// <see cref="MailDeliveryCapabilities.BeforeAnyServerHasSpoken" /> for the reason a new message is: the server that
/// will carry it is not being talked to and must not be, and what that server does decide is asked again by the
/// delivery pass against the length that was stored.
/// </para>
/// </remarks>
/// <param name="authoring">Turns the stored email and what somebody wrote into the message to compose.</param>
/// <param name="composer">Builds the MIME, and decides every header this system owns rather than the author.</param>
/// <param name="outbox">Writes the record and the message down together, and says the account has something to send.</param>
/// <param name="governor">Answers what this caller may be talked into sending, and records the send once it is durable.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it send.</param>
public sealed class AuthoredResponseSubmission(
    StoredEmailResponseAuthoring authoring,
    IAuthoredEmailComposer composer,
    MailOutbox outbox,
    AuthoredSendGovernor governor,
    AccessAuthorization authorization)
{
    /// <summary>Queues one answer to a stored email, or refuses it naming what the caller has to change.</summary>
    /// <param name="request">The answer that was asked for.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>The durable record the answer was written down as, whether this call created it or an identical earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds neither <see cref="MailFathomPermission.MailSend" /> nor, beneath it, <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="MailSubmissionRefusedException">Thrown when the answered email cannot be answered, a recipient the author added names nobody, a field cannot be composed, or a bound is exceeded.</exception>
    /// <exception cref="OutgoingMailRefusedException">Thrown when a recipient is one this deployment may not write to, when this caller has reached a ceiling of its own, or when a recipient it added itself is one nothing here vouches for.</exception>
    public async Task<OutgoingEmailRecord> SubmitAsync(
        MailResponseSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        authorization.RequirePermission(MailFathomPermission.MailSend);

        // Ahead of the authoring rather than left to it, for the reason a new message's list is checked ahead of the
        // resolution: the reads it performs carry what the caller supplied, and a list this long describes an answer no
        // record could be written for however the book answered.
        if (request.Recipients.Count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailSubmissionRefusedException.TooManyRecipients();
        }

        var response = await authoring.AuthorAsync(
            new AuthoredResponseRequest
            {
                AnsweredEmailId = request.AnsweredEmailId,
                Act = request.Act,
                PlainTextBody = request.PlainTextBody,
                HtmlBody = request.HtmlBody,
                Recipients = request.Recipients,
            },
            cancellationToken);

        if (response.Refusal is { } refusal)
        {
            throw MailSubmissionRefusedException.From(refusal);
        }

        var composition = composer.Compose(
            response.Account,
            request.Requester,
            response.Email!,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Email is not { } composed)
        {
            throw MailSubmissionRefusedException.From(composition.Refusal!);
        }

        // The people an answer is addressed to are mostly this system's reading of the message being answered, and the
        // governance beneath knows which is which: what a caller added itself is judged as its own word, and the rest
        // is judged by the deployment's recipient policy exactly as it is on a message answering nothing.
        var permit = await governor.RequirePermittedAsync(
            response.Email!.Recipients,
            composed.Request,
            cancellationToken);

        var opened = await outbox.EnqueueAsync(composed.Request, composed.RawMime, cancellationToken);

        // Only where this call is what wrote the record down, for the reason the same line carries on a new message: a
        // retry answered from the record an earlier call left is one answer sent once, and a second audit entry for it
        // would say otherwise.
        if (opened.WasRecordedNow)
        {
            await governor.RecordAsync(permit, ActOf(request.Act), opened.Record, cancellationToken);
        }

        return opened.Record;
    }

    /// <summary>Names the send an answer is, in the terms the record of it is written in.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the act is one this method was never taught, which is a defect here rather than a refusal.</exception>
    private static AuthoredSendAct ActOf(AuthoredResponseAct act) => act switch
    {
        AuthoredResponseAct.Reply => AuthoredSendAct.Reply,
        AuthoredResponseAct.ReplyToAll => AuthoredSendAct.ReplyToAll,
        AuthoredResponseAct.Forward => AuthoredSendAct.Forward,
        _ => throw new ArgumentOutOfRangeException(
            nameof(act),
            act,
            "The authored response act names no send this system records."),
    };
}

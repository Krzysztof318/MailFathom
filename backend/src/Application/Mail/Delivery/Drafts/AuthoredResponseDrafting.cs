// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Takes an answer somebody wrote to a stored email and holds it as a draft.</summary>
/// <remarks>
/// <para>
/// It is the counterpart of <see cref="AuthoredMailDrafting" /> for the two answers that begin from mail this
/// deployment already holds, and it is a sibling of <see cref="Submission.AuthoredResponseSubmission" /> rather than a
/// mode of it. The threading identifiers, the quoted original, the carried attachments, and the subject prefix are
/// <see cref="StoredEmailResponseAuthoring" />'s, so what reaches the composition is an ordinary authored message,
/// indistinguishable at that point from one that answers nothing.
/// </para>
/// <para>
/// A revision re-authors from the answered email rather than from what the previous revision produced. That is what
/// keeps a draft of a reply a reply after it is edited: the identifiers a client threads by come out of the stored copy
/// every time, so an edit can neither lose them nor invent them.
/// </para>
/// <para>
/// Two grants are asked for, in this order. Drafting is refused first, because a caller that may not draft has no
/// business reaching a use case that reads the mail it would have quoted; reading is refused by the authoring beneath,
/// because an answer quotes the message it answers and a forward carries that message's files. Neither of them is the
/// sending grant: what this leaves is a message nobody has been offered.
/// </para>
/// </remarks>
/// <param name="authoring">Turns the stored email and what somebody wrote into the message to compose.</param>
/// <param name="composer">Builds the MIME, and decides every header this system owns rather than the author.</param>
/// <param name="drafts">Writes the record and the message down together, and brings the drafts folder into step.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it draft.</param>
public sealed class AuthoredResponseDrafting(
    StoredEmailResponseAuthoring authoring,
    IAuthoredEmailComposer composer,
    MailDraftBook drafts,
    AccessAuthorization authorization)
{
    /// <summary>Holds one answer to a stored email as a draft, or refuses it naming what the caller has to change.</summary>
    /// <param name="request">The draft that was asked for.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns>The draft as it stands once the mailbox has been brought into step with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller holds neither <see cref="MailFathomPermission.MailDraftsWrite" /> nor, beneath it, <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when the answered email cannot be answered, a recipient the author added names nobody, a field cannot be composed, a bound is exceeded, or the draft being revised is not one of that account's.</exception>
    public async Task<MailDraftRecord> SaveAsync(
        MailResponseDraftRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        // Ahead of the authoring rather than left to it, for the reason a new message's list is checked ahead of the
        // resolution: the reads it performs carry what the caller supplied.
        if (request.Recipients.Count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailDraftRefusedException.TooManyRecipients();
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
            throw MailDraftRefusedException.From(refusal);
        }

        var composition = composer.ComposeDraft(
            response.Account,
            response.Email!,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Draft is not { } composed)
        {
            throw MailDraftRefusedException.From(composition.Refusal!);
        }

        return await drafts.SaveAsync(
            response.Account,
            request.Author,
            composed,
            request.Revises,
            cancellationToken);
    }
}

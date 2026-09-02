// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>The failure raised when a draft somebody asked for is not stored, not revised, or not promoted.</summary>
/// <remarks>
/// <para>
/// It is the sibling of the submission's own refusal and shares every reason with it, because a draft is a message and
/// is refused for everything a message is refused for. What the two do not share is a name for the act: a caller told
/// its <em>submission</em> was refused when it asked to save a draft would read that as a message having been offered
/// to a server and turned down, which is the one thing that certainly did not happen. Both publish through
/// <see cref="AuthoredMailRefusalPublication" />, so the code and the sentence a given mistake produces are one answer.
/// </para>
/// <para>
/// Two refusals are the draft's own. A draft nobody holds is the not-found answer every identifier in this system
/// gives, and a draft nobody is addressed to is the one thing a draft may be and a send may not — so the absence of a
/// recipient is refused here, at the promotion, rather than when the draft was written.
/// </para>
/// <para>
/// Three more are the draft's own for a different reason: a draft is the one authored act that comes in two shapes, a
/// message of its own and an answer to a stored email, and a request that describes both or neither describes no draft
/// this system can write. They publish the field refusal every authored message shares, because what a caller does
/// about them is what that code is for — send the fields differently.
/// </para>
/// </remarks>
public sealed class MailDraftRefusedException : MailFathomException
{
    private MailDraftRefusedException(PublishedMailRefusal refusal)
        : base(refusal.Message) => this.ErrorCode = refusal.Code;

    private MailDraftRefusedException(MailFathomErrorCode errorCode, string clientSafeMessage)
        : base(clientSafeMessage) => this.ErrorCode = errorCode;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Reports a draft that was not composed, in the terms the author can act on.</summary>
    /// <param name="refusal">What the composition refused and where.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare.</exception>
    public static MailDraftRefusedException From(AuthoredEmailRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports a recipient that resolved to nobody, naming what was counted and never who.</summary>
    /// <param name="refusal">What the resolution refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare.</exception>
    public static MailDraftRefusedException From(RecipientResolutionRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports an answer to a stored email that was not authored at all.</summary>
    /// <param name="refusal">What the authoring refused.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the refusal names a reason this system does not declare.</exception>
    public static MailDraftRefusedException From(AuthoredResponseRefusal refusal) =>
        new(AuthoredMailRefusalPublication.Of(refusal));

    /// <summary>Reports a list of recipients longer than any outgoing record could be written for.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailDraftRefusedException TooManyRecipients() =>
        new(AuthoredMailRefusalPublication.TooManyRecipients());

    /// <summary>Reports text that names no account this deployment could look for.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailDraftRefusedException AccountNotNamed() =>
        new(AuthoredMailRefusalPublication.AccountNotNamed());

    /// <summary>Reports that no draft this deployment holds answers to the identifier a caller named.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is also what asking to delete or revise a draft this system did not create produces, and that is the point
    /// rather than a side effect: a draft somebody wrote in their own mail client is held under no identifier of
    /// MailFathom's, so there is nothing here that could name it and nothing that could remove it.
    /// </remarks>
    public static MailDraftRefusedException NotFound() => new(
        MailFathomErrorCode.MailDraftNotFound,
        "No draft this deployment holds is kept under that identifier.");

    /// <summary>Reports a request that names the email a draft answers without naming which answer it is, or the other way round.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The two go together because neither states an answer on its own: a reply, a reply to all, and a forward reach
    /// three different sets of people from one stored email, and an email nobody is answering is not a message at all.
    /// It is the field refusal every authored message shares rather than a code of its own, because the remedy is the
    /// one that code exists for — write the fields differently.
    /// </remarks>
    public static MailDraftRefusedException AnsweredEmailAndAnswerDisagree() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        "A draft answering a stored email names the email and names which answer it is, or names neither.");

    /// <summary>Reports a drafted answer naming none of the answers a stored email can be answered with.</summary>
    /// <returns>The failure to raise.</returns>
    public static MailDraftRefusedException AnswerUnknown() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        "A draft answering a stored email states whether it replies to the sender alone, replies to everybody the message was between, or forwards it, and it named none of the three.");

    /// <summary>Reports a drafted answer that also states an account or a subject of its own.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Both are read from the stored email being answered, so a request that states one describes two different
    /// messages and this system would have to pick. Refusing is what keeps an answer an answer: the account decides
    /// which mailbox the draft belongs to, and taking the caller's would let a draft be written into a mailbox the
    /// answered email is not in.
    /// </remarks>
    public static MailDraftRefusedException AnsweredDraftStatesItsOwnMessage() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        "A draft answering a stored email takes neither an account nor a subject, because both are read from the email it answers.");

    /// <summary>Reports a draft of a message of its own that states no account or no subject.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// A draft that answers nothing has nowhere else to read them from. The subject may be empty text, which is a
    /// message somebody has not titled yet; what is refused here is the field being absent, which says the caller
    /// meant the other shape.
    /// </remarks>
    public static MailDraftRefusedException MessageNotStated() => new(
        MailFathomErrorCode.AuthoredMailFieldRefused,
        "A draft that answers no stored email states the account it belongs to and the subject of its message.");

    /// <summary>Reports a draft that was asked to be sent and names nobody to send it to.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// Writing the message before deciding who reads it is what a draft is for, so this is refused at the promotion
    /// rather than when the draft was saved. The remedy is a revision that addresses it, which leaves the message the
    /// author already wrote exactly as it is.
    /// </remarks>
    public static MailDraftRefusedException NotAddressed() => new(
        MailFathomErrorCode.MailDraftNotAddressed,
        "The draft names nobody to send it to, so there is no message to queue.");

    /// <summary>Reports a draft this deployment will not put on a mail server, because of what the message carries.</summary>
    /// <param name="refusal">What the screen stopped it with.</param>
    /// <returns>The failure to raise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refusal" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the refusal names a reason this system does not declare.</exception>
    /// <remarks>
    /// The two sentences are this act's own rather than the submission's, for the reason every refusal here is: a
    /// caller told its <em>message</em> was not queued when it asked to save a draft would go looking for a send that
    /// never happened. The code is shared with the submission, because the remedy is the same one.
    /// </remarks>
    public static MailDraftRefusedException ContentRefused(SensitiveContentEgressRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);

        return refusal.Reason switch
        {
            SensitiveContentEgressRefusalReason.ContentFound => new MailDraftRefusedException(
                MailFathomErrorCode.OutgoingMailContentRefused,
                $"This deployment screens what it puts on a mail server, and this message carries content it classes as {refusal.Category}, so no draft was written. Take that material out of the message and save it again."),
            SensitiveContentEgressRefusalReason.TextExceededScanCeiling => new MailDraftRefusedException(
                MailFathomErrorCode.OutgoingMailNotFullyScanned,
                "This message is longer than one sensitive-content scan analyzes, so nothing established what all of it carries and no draft was written. Save a shorter message, or ask the operator to raise the analyzed ceiling."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal.Reason,
                "The sensitive-content egress refusal reason is not one this system declares."),
        };
    }
}

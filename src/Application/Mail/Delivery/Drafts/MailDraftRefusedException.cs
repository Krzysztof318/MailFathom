// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
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
}

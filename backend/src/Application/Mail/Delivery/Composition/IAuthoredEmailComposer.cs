// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Turns what an author wrote into the message a submission server will be offered.</summary>
/// <remarks>
/// <para>
/// This is the one place a MIME message is built, and the boundary the mail library stops at: nothing above it names a
/// MIME type, and nothing below it sees an authored request. A second path that assembled headers of its own would be
/// a second set of answers to the questions this one settles — who the message is from, what identity it carries, what
/// a newline in a subject does — and the first of those to disagree is a message sent as somebody else.
/// </para>
/// <para>
/// Composing reaches no network and no database. What it needs from the submission server is what that server already
/// said when a session was opened, which arrives as a parameter for that reason: the answer belongs to one connection
/// to one endpoint, and a composer that cached it would bound a message against a server it is no longer talking to.
/// </para>
/// </remarks>
public interface IAuthoredEmailComposer
{
    /// <summary>Composes one authored message, or refuses it naming the field that stopped it.</summary>
    /// <param name="accountId">The account the message is sent as, which decides the <c>From</c> address.</param>
    /// <param name="requester">The authored act asking, which is what makes the same request twice one delivery.</param>
    /// <param name="authored">What the author wrote.</param>
    /// <param name="capabilities">What the submission server said it will accept, read from the session that will carry the message.</param>
    /// <returns>The composed message, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" />, <paramref name="authored" />, or <paramref name="capabilities" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a recipient names a role this system does not declare, which is a boundary that mapped its input wrongly rather than an author who wrote something wrong.</exception>
    AuthoredEmailComposition Compose(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        AuthoredEmail authored,
        MailDeliveryCapabilities capabilities);

    /// <summary>Composes one occasion's message from the draft a recurring send was declared with, or refuses it.</summary>
    /// <param name="accountId">The account the message is sent as, which decides the <c>From</c> address again.</param>
    /// <param name="requester">The occasion asking, which is what makes one occasion's message one delivery.</param>
    /// <param name="recipients">The people this occasion is offered to, as the declaration recorded them.</param>
    /// <param name="draftMime">The stored draft, exactly as the declaration was made with.</param>
    /// <param name="capabilities">What the submission server said it will accept, or what holds before one has spoken.</param>
    /// <returns>The composed message, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" />, <paramref name="recipients" />, or <paramref name="capabilities" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="draftMime" /> is empty.</exception>
    /// <remarks>
    /// <para>
    /// Every occasion is a message of its own, and this is where that becomes true rather than aspirational: the draft
    /// is stamped with an identity and a date of this occasion's, so a year of Mondays is a year of messages instead of
    /// one message a client threads over itself and a server may refuse as a duplicate.
    /// </para>
    /// <para>
    /// What the draft says the message is from is replaced rather than kept, because a message is sent as the account
    /// that sends it and an operator may have changed what that account writes as since the declaration was made. The
    /// rest of the draft is transmitted as it stands, hidden recipients included: the addresses this occasion is
    /// offered to come from the declaration beside it, because a blind recipient is by construction not in the bytes.
    /// </para>
    /// <para>
    /// The bounds are applied again here, against the message this occasion actually became. A deployment that has
    /// since tightened what it will send refuses the occasion rather than transmitting under the bound that was in
    /// force when somebody wrote the draft.
    /// </para>
    /// </remarks>
    AuthoredEmailComposition RecomposeAsOccurrence(
        MailAccountId accountId,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        ReadOnlyMemory<byte> draftMime,
        MailDeliveryCapabilities capabilities);

    /// <summary>Composes one authored message as a draft, or refuses it naming the field that stopped it.</summary>
    /// <param name="accountId">The account the draft belongs to, which decides the <c>From</c> address it would be sent under.</param>
    /// <param name="authored">What the author wrote.</param>
    /// <param name="capabilities">What a submission server would accept, which for a draft is what holds before any server has spoken.</param>
    /// <returns>The composed draft, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authored" /> or <paramref name="capabilities" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a recipient names a role this system does not declare, which is a boundary that mapped its input wrongly rather than an author who wrote something wrong.</exception>
    /// <remarks>
    /// <para>
    /// It is the same composition with one difference, and the difference is the only one a draft earns: a draft
    /// addressed to nobody is composed rather than refused. Writing the message before deciding who reads it is what a
    /// draft is for, and the absence is refused where the send would be written down instead.
    /// </para>
    /// <para>
    /// There is no requester, because a draft is not written down under an idempotency identity: asking twice for a
    /// draft leaves two drafts, and neither of them can reach anybody. Every other decision this composition owns — the
    /// sending address, the minted identity, the date, the refusal of an injected header, and every bound — is the same
    /// one a send meets, so a draft that is promoted is a message this deployment had already agreed to compose.
    /// </para>
    /// </remarks>
    MailDraftComposition ComposeDraft(
        MailAccountId accountId,
        AuthoredEmail authored,
        MailDeliveryCapabilities capabilities);
}

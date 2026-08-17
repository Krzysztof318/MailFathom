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
}

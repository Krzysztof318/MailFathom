// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>States what stopped an answer from being authored, before any of it was composed.</summary>
/// <remarks>
/// Every one of these is about the message being answered or about the account answering it, which is what separates
/// them from the composition's own refusals: those name a field the author can correct, and none of these can be
/// corrected by writing the message differently.
/// </remarks>
public enum AuthoredResponseRefusalReason
{
    /// <summary>There is no such stored email to answer, as far as this caller can be told.</summary>
    /// <remarks>
    /// It is one answer for three situations — no such identity, an account this deployment no longer serves, and a
    /// folder an operator withheld from tools — because it is the same answer a read of that email gives. Telling them
    /// apart would let a caller discover which mail exists by trying to reply to it, and an email nothing may read is
    /// an email nothing may forward.
    /// </remarks>
    AnsweredEmailNotFound = 0,

    /// <summary>The answered email's content is not something this deployment can read, so there is nothing to quote or carry.</summary>
    /// <remarks>
    /// Content synchronization deliberately left unstored, a local copy that has gone missing or is damaged, bytes that
    /// no longer parse, and a body inside a cryptographic envelope all arrive here. The alternative is worse in every
    /// one of them: an answer quoting nothing looks like an answer to an empty message, and a forward of a message
    /// whose parts could not be read delivers a shell of one.
    /// </remarks>
    AnsweredEmailContentUnavailable = 1,

    /// <summary>The account the answer would be sent as configures no address to send from.</summary>
    /// <remarks>
    /// It is refused here as well as during composition, because the sending address is what decides which mailboxes a
    /// reply to all must leave out. Composing without it would mail the account its own reply, which is the loop the
    /// exclusion exists to prevent.
    /// </remarks>
    SenderUnconfigured = 2,

    /// <summary>What the answer would carry exceeds a bound this deployment composes within.</summary>
    /// <remarks>
    /// A forward is where this happens: the files belong to the original rather than to whoever is forwarding it, so
    /// the answer is the only place their number and size can be judged. The refusal names the bound and never the
    /// message.
    /// </remarks>
    BoundExceeded = 3,

    /// <summary>A recipient the author added named a contact the book does not hold.</summary>
    /// <remarks>
    /// The three reasons below are the resolution's own, restated here because an author of an answer meets them exactly
    /// as an author of a message answering nothing does. Which of the three it is decides what the author changes, which
    /// is why one reason for all of them would not do.
    /// </remarks>
    RecipientContactUnknown = 4,

    /// <summary>A recipient the author added named a contact by a name more than one contact carries.</summary>
    /// <remarks>The refusal carries how many matched, so the author knows the name is shared rather than wrong.</remarks>
    RecipientContactNameAmbiguous = 5,

    /// <summary>A recipient the author added chose an address the named contact does not hold.</summary>
    RecipientContactAddressNotHeld = 6,
}

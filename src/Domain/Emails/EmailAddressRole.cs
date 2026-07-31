// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Domain.Emails;

/// <summary>States which mail header an address was written in.</summary>
/// <remarks>
/// The role is kept rather than flattened into one participant list because the header an address appeared in is what a
/// later filter means: "from this person" and "copied to this person" are different questions about the same address.
/// </remarks>
public enum EmailAddressRole
{
    /// <summary>The <c>Sender</c> header, which names who submitted a message written on someone else's behalf.</summary>
    Sender = 0,

    /// <summary>The <c>From</c> header, which names the author.</summary>
    From = 1,

    /// <summary>The <c>Reply-To</c> header, which names where a reply is meant to go.</summary>
    ReplyTo = 2,

    /// <summary>The <c>To</c> header, which names the primary recipients.</summary>
    To = 3,

    /// <summary>The <c>Cc</c> header, which names the carbon-copied recipients.</summary>
    Cc = 4,

    /// <summary>The <c>Bcc</c> header, which is present only in a copy the sender kept.</summary>
    Bcc = 5,
}

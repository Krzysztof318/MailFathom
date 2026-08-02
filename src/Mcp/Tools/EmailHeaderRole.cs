// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools;

/// <summary>Names the mail header one published address was written in, as the protocol spells it.</summary>
/// <remarks>
/// The roles stay apart on the wire because "wrote this message", "was copied on it", and "is where a reply goes" are
/// different facts about the same address, and a reader answering a question about a message needs to tell them apart.
/// </remarks>
internal enum EmailHeaderRole
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

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Which of an exchange's two participants wrote one message.</summary>
/// <remarks>
/// An exchange has exactly two sides and they alternate, so the side is a message's position within its thread rather
/// than a value drawn for it. It is named instead of left as a parity check because it decides three different things
/// — whose address the message is from, whether it is submitted or appended, and where its identifier comes from —
/// and a reader following any one of those should not have to recover the other two from an index.
/// </remarks>
internal enum SyntheticThreadSide
{
    /// <summary>The invented correspondent, whose messages the sending account submits to the watched mailbox.</summary>
    Correspondent = 0,

    /// <summary>The mailbox MailFathom synchronizes, whose messages this run appends to that mailbox's Sent folder.</summary>
    Mailbox = 1,
}

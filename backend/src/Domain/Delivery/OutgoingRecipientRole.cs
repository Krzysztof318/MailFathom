// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>States which header a recipient of an outgoing email is named in.</summary>
/// <remarks>
/// The three are the whole of what an envelope is built from, which is why this is its own set rather than the header
/// role a received message's addresses carry: that one also names an author, a sender, and a reply address, and none of
/// those is somebody a message is offered to. Every member here reaches <c>RCPT TO</c> identically — the role decides
/// what the composed message says about a recipient, never whether the server is asked to accept them.
/// </remarks>
public enum OutgoingRecipientRole
{
    /// <summary>The <c>To</c> header, which names a primary recipient.</summary>
    To = 0,

    /// <summary>The <c>Cc</c> header, which names a carbon-copied recipient.</summary>
    Cc = 1,

    /// <summary>The <c>Bcc</c> header, which names a recipient the other recipients are not told about.</summary>
    /// <remarks>
    /// The distinction is the composed message's rather than the envelope's: a blind recipient is offered exactly as
    /// any other is, and what makes them blind is that the transmitted headers do not name them.
    /// </remarks>
    Bcc = 2,
}

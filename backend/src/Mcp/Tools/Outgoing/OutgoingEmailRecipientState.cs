// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes what a mail server has said about one recipient of a queued message.</summary>
/// <remarks>
/// A message is offered per address and answered per address, so one recipient's outcome is not the message's: a send
/// can reach four people and be refused for a fifth. These three are what a later attempt does about each — offer them
/// again, never offer them again because they have it, never offer them again because they will not get it.
/// </remarks>
internal enum OutgoingEmailRecipientState
{
    /// <summary>No answer has settled this recipient, so an attempt still offers the message to them.</summary>
    [Description("Nothing has settled this recipient yet, so the message is still to be offered to them. On a message that has stopped being attempted it means no server ever answered about this address.")]
    Pending = 0,

    /// <summary>The transmission that carried this recipient was acknowledged.</summary>
    [Description("The message was transmitted with this recipient accepted, and nothing will offer it to them again.")]
    Accepted = 1,

    /// <summary>A mail server permanently refused this recipient.</summary>
    [Description("A mail server permanently refused this address. The message does not reach them, and nothing will offer it to them again.")]
    Refused = 2,
}

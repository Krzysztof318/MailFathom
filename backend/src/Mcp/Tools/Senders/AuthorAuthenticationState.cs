// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports what the receiving mail server established about the author an email displays, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration for the reason every other published one does: the member names are the
/// wire values, so they belong to the boundary that publishes them rather than to the domain that happens to describe
/// the same states today.
/// </remarks>
internal enum AuthorAuthenticationState
{
    /// <summary>Nothing trusted was enough to conclude either way about the displayed author.</summary>
    NotEstablished = 0,

    /// <summary>The receiving server evaluated the displayed author under its own domain's policy and it did not hold.</summary>
    Failed = 1,

    /// <summary>The receiving server established that the displayed author authenticated.</summary>
    Authenticated = 2,
}

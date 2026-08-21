// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports which check established an email's authenticated domain, as the protocol spells it.</summary>
/// <remarks>
/// The wire values are the two protocol names a reader already knows, rather than the domain's spelled-out member
/// names: <c>dkim</c> and <c>spf</c> are how RFC 8601 writes them and how anybody reading a mail header meets them.
/// </remarks>
internal enum SenderAuthenticationCheck
{
    /// <summary>No identity was established, so nothing named one.</summary>
    None = 0,

    /// <summary>A DKIM signature verified against a key the signing domain publishes.</summary>
    Dkim = 1,

    /// <summary>The envelope sender passed the SPF policy the connecting address was checked against.</summary>
    Spf = 2,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Outgoing;

/// <summary>Publishes which header of the message named one recipient.</summary>
/// <remarks>
/// It is the surface's own set rather than the domain enumeration describing the same three, because the member names
/// are the published wire values: a rename inside the domain would otherwise be a silent change to this contract.
/// </remarks>
internal enum OutgoingEmailRecipientHeader
{
    /// <summary>The <c>To</c> header.</summary>
    [Description("The person was named in the To header, as a primary recipient.")]
    To = 0,

    /// <summary>The <c>Cc</c> header.</summary>
    [Description("The person was named in the Cc header, carbon-copied where every other recipient can see them.")]
    Cc = 1,

    /// <summary>The <c>Bcc</c> header.</summary>
    [Description("The person was blind-copied: they receive the message and no other recipient is told that they did.")]
    Bcc = 2,
}

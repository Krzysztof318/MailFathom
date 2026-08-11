// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Content;

/// <summary>The published values saying whether a link to fetch an attachment came back, and why it did not.</summary>
/// <remarks>
/// The member names are the wire values, camel-cased by the one serialization policy every tool registration is given.
/// The type is this boundary's own rather than the application enumeration describing the same states, so a rename
/// inside the application is not a silent change to the protocol.
/// </remarks>
internal enum EmailAttachmentDownloadState
{
    /// <summary>A link was issued and is redeemable until it expires.</summary>
    Issued = 0,

    /// <summary>The call did not ask for links, so the file was described and no capability was minted.</summary>
    NotRequested = 1,

    /// <summary>This deployment issues no attachment links at all, whatever a call asks for.</summary>
    Unavailable = 2,
}

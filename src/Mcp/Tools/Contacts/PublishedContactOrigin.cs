// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>States how a contact came to be in the book, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration rather than the domain's, so the published wire values are decided here and
/// a rename inside the domain stays a rename. The member names are the wire values — they are serialized camel-cased —
/// which is exactly why they cannot be shared with a type whose names exist to describe the domain.
/// </remarks>
internal enum PublishedContactOrigin
{
    /// <summary>Somebody wrote this person down.</summary>
    Asserted = 0,

    /// <summary>An address that appeared in mail that arrived.</summary>
    Collected = 1,
}

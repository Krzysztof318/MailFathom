// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailMcp.Mcp.Tools;

/// <summary>Selects which end of the timeline a listing reads from, as the protocol spells it.</summary>
/// <remarks>
/// The transport carries its own enumeration rather than the domain's, so the published wire values are decided here and
/// a rename inside the domain stays a rename. The member names are the wire values — they are serialized camel-cased —
/// which is exactly why they cannot be shared with a type whose names exist to describe the domain.
/// </remarks>
internal enum ListEmailsDirection
{
    /// <summary>Reads the most recently received email first.</summary>
    NewestFirst = 0,

    /// <summary>Reads the least recently received email first.</summary>
    OldestFirst = 1,
}

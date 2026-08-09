// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Content;

/// <summary>The published values saying whether an attachment's content came back, and which bound stopped it.</summary>
/// <remarks>
/// The member names are the wire values, camel-cased by the one serialization policy every tool registration is given.
/// The type is this boundary's own rather than the application enumeration describing the same states, so a rename
/// inside the application is not a silent change to the protocol.
/// </remarks>
internal enum EmailAttachmentContentState
{
    /// <summary>The content is present and is the whole file.</summary>
    Returned = 0,

    /// <summary>The file is larger than one attachment may return, so none of it was returned.</summary>
    ExceededAttachmentByteLimit = 1,

    /// <summary>The call's attachment budget was spent by the attachments returned before this one.</summary>
    ReadByteBudgetExhausted = 2,
}

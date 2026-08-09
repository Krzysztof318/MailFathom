// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Says whether a read returned an attachment's content, and which bound stopped it when it did not.</summary>
/// <remarks>
/// The bound is named rather than merely reported, for the reason <see cref="EmailBodyTruncation" /> names the bound
/// that cut a body: the two absences lead a caller to different actions. A file above the per-attachment ceiling is
/// above it in every call, and a file the read's budget did not reach comes back when it is asked for on its own.
/// </remarks>
public enum EmailAttachmentContentAvailability
{
    /// <summary>The content is present and is the whole of what the part decoded to.</summary>
    Returned = 0,

    /// <summary>The attachment decodes to more octets than one attachment may return, so none of it was returned.</summary>
    ExceededAttachmentByteLimit = 1,

    /// <summary>The read's attachment budget was spent by the attachments returned before this one.</summary>
    ReadByteBudgetExhausted = 2,

    /// <summary>Nothing asked for this attachment's content, so nothing decoded it.</summary>
    /// <remarks>
    /// It never reaches a reader: a rendering given no attachment bounds publishes no attachment list at all, and this
    /// is what the parse that fills the lexical index leaves behind on the descriptions it produces along the way.
    /// </remarks>
    NotRequested = 3,
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Application.EmailContent;

/// <summary>Bounds what one read of a message body may return.</summary>
/// <remarks>
/// The bound is separate from the one extraction applies to the text it indexes, because the two protect different
/// things. The indexing bound exists so a generated search vector stays writable; this one exists so a single response
/// cannot overflow the context of whatever is reading it, and so a body that reaches this system through no limit of
/// its own reaches a reader through one.
/// </remarks>
public sealed class EmailContentReadOptions
{
    /// <summary>Gets or sets the greatest number of characters one body representation returns.</summary>
    /// <remarks>
    /// It applies to each representation separately: a message can exceed it in its HTML and not in its plain text, and
    /// bounding them together would make one representation's length decide what the other returned.
    /// </remarks>
    public int MaxBodyCharacters { get; set; } = 100_000;
}

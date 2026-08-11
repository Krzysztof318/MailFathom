// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent;

/// <summary>Bounds what one read of message content may return.</summary>
/// <remarks>
/// <para>
/// The bound is separate from the one extraction applies to the text it indexes, because the two protect different
/// things. The indexing bound exists so a generated search vector stays writable; these exist so a single response
/// cannot overflow the context of whatever is reading it, and so a body that reaches this system through no limit of
/// its own reaches a reader through one.
/// </para>
/// <para>
/// Two bounds rather than one, because a call names several emails and the count and the volume are different controls.
/// Without the second, ten emails could each return the first bound in full; without the first, one enormous message
/// could spend a whole call's budget before the second email was reached.
/// </para>
/// <para>
/// Attachments are bounded by neither, because a read returns none of their octets: what a caller receives for a file
/// is a link to fetch it, whose size is the same few hundred characters whatever the file weighs. The only bound an
/// attachment is subject to is the one that decided whether its message was stored at all.
/// </para>
/// </remarks>
public sealed class EmailContentReadOptions
{
    /// <summary>Gets or sets the greatest number of characters one body representation returns.</summary>
    /// <remarks>
    /// It applies to each representation separately: a message can exceed it in its HTML and not in its plain text, and
    /// bounding them together would make one representation's length decide what the other returned.
    /// </remarks>
    public int MaxBodyCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets the greatest number of body characters one call returns across every email it names.</summary>
    /// <remarks>
    /// <para>
    /// It is consumed in the order the emails were named, so a call whose first emails are large returns less of the
    /// later ones and says so on each representation it had to cut. Nothing a request carries can raise it: it is the
    /// deployment's control over how much mail one protocol call can draw out of a mailbox.
    /// </para>
    /// <para>
    /// The default is twice <see cref="MaxBodyCharacters" />, and the host refuses a configuration below that. A single
    /// email asking for both representations may return the per-representation bound twice, so a smaller budget would
    /// cut a one-email call by a limit that exists for calls naming several.
    /// </para>
    /// </remarks>
    public int MaxCharactersPerRead { get; set; } = 200_000;
}

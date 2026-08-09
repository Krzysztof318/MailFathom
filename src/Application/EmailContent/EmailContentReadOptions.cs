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
/// Attachment content is bounded by the same pair again, in octets rather than characters, and the two pairs are spent
/// independently. Text and files are different quantities a caller asks for separately, so a message carrying a large
/// file must not shorten the bodies of the emails named after it, and a long thread must not withhold a file.
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

    /// <summary>Gets or sets the greatest number of decoded octets one attachment returns.</summary>
    /// <remarks>
    /// <para>
    /// An attachment above it is described exactly as it would be otherwise and carries no content, because a file is
    /// returned whole or not at all. Zero therefore returns no attachment content at all, which is how a deployment
    /// that wants attachments described and never handed over says so.
    /// </para>
    /// <para>
    /// It bounds a response rather than this process: an attachment can only be as large as the raw MIME it arrived in,
    /// which <c>MailSynchronization:MaxRawMimeBytes</c> already limits. What it decides is how much of it a caller is
    /// handed, and — since the wire form is base64 — a third again as much of the response it is handed in.
    /// </para>
    /// </remarks>
    public int MaxAttachmentBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Gets or sets the greatest number of attachment octets one call returns across every email it names.</summary>
    /// <remarks>
    /// <para>
    /// It is consumed in the order the emails were named and, within an email, in the order the message's structure was
    /// walked. An attachment reached after it is spent is described with no content and says so, exactly as one above
    /// the per-attachment bound does.
    /// </para>
    /// <para>
    /// The host refuses a configuration below <see cref="MaxAttachmentBytes" />, because a budget that cannot carry a
    /// single permitted attachment would withhold on behalf of a bound the operator set the other value to allow.
    /// </para>
    /// </remarks>
    public int MaxAttachmentBytesPerRead { get; set; } = 10 * 1024 * 1024;
}

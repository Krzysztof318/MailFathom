// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Retrieval;

/// <summary>One bounded extract of a file a message carried, with the coordinate an answer cites it by.</summary>
/// <remarks>
/// <para>
/// Beside the message's own extract rather than joined to it, because a model told a contract's words were the body's
/// would attribute them to whoever wrote the covering note. What the coordinate buys is the other half: a claim drawn
/// from page fourteen of a report is checkable, and the same claim attributed to "an attachment" is not.
/// </para>
/// <para>
/// <see cref="Kind" /> decides how the text is framed for a model rather than merely labelling it. A document's words
/// are quoted evidence a stranger wrote; a description is a machine's account of a picture, which nobody wrote at all,
/// and a model reading it unattributed would report that somebody said what the picture shows and cite a message for it.
/// It is untrusted on top of that: a hostile sender can compose an image whose description is an instruction, and text
/// arriving back from this deployment's own provider is no more trustworthy than the bytes it was derived from.
/// </para>
/// <para>
/// The text is mail content and is bounded before this record exists, so nothing downstream can widen it. It is never
/// logged, never attached to a span, and never exported.
/// </para>
/// </remarks>
/// <param name="AttachmentPosition">The zero-based walk position of the attachment, which is what a download is addressed with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="Kind">Whether the words are the file's own or a model's account of a picture.</param>
/// <param name="Segment">The page, slide, or sheet it was read from, or <see langword="null" /> where the reading recorded no boundaries.</param>
/// <param name="Text">The extract itself, already cut to the size one passage may carry.</param>
public sealed record EmailKnowledgeAttachmentExtract(
    int AttachmentPosition,
    string? FileName,
    AttachmentTextKind Kind,
    AttachmentTextSegment? Segment,
    string Text);

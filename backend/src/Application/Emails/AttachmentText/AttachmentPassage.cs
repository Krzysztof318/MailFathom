// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>One passage cut out of an attachment, with the place inside the file it was read from.</summary>
/// <remarks>
/// <para>
/// <b>This is the one kind of passage a caller may receive directly, and a message passage is not.</b> A message chunk
/// exists purely to be embedded — anything that needs a message reads it whole, because a message is small enough that
/// reading it whole is always the right answer. An attachment is not: a two-hundred-page report re-served in full every
/// time a search cites it is neither necessary nor, for a model composing an answer, useful. A few hundred words around
/// the words that matched, with a pointer to where in the file they were, is what an answer quotes and what a person
/// verifies.
/// </para>
/// <para>
/// The text is mail content, and where <see cref="Kind" /> is a description it is a machine's account of mail content —
/// untrusted twice over. Whoever presents it or hands it to a model treats it as opaque characters a stranger composed,
/// and it reaches no log, metric, trace, or error message.
/// </para>
/// </remarks>
/// <param name="AttachmentPosition">The zero-based walk position of the attachment, which is what the download route is addressed with.</param>
/// <param name="Ordinal">The passage's place in that attachment's own text, counted from zero in reading order.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="DeclaredMediaType">What the part declares itself to be, which is the sender's claim rather than a reading of the content.</param>
/// <param name="Kind">Whether the words are the file's own or a model's account of a picture.</param>
/// <param name="Segment">
/// The page, slide, or sheet the passage begins in, or <see langword="null" /> where the reading recorded no
/// boundaries — an attachment whose text a redaction rewrote to a different length is the case that produces one,
/// because a boundary that no longer indexes the words would send a reader to the wrong page.
/// </param>
/// <param name="Text">The passage itself, exactly as the chunker cut it.</param>
public sealed record AttachmentPassage(
    int AttachmentPosition,
    int Ordinal,
    string? FileName,
    string DeclaredMediaType,
    AttachmentTextKind Kind,
    AttachmentTextSegment? Segment,
    string Text);

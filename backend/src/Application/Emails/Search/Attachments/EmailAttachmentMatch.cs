// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Emails.Search.Attachments;

/// <summary>Why one of a message's attachments is part of a search result, and where inside the file that is.</summary>
/// <remarks>
/// <para>
/// A match of its own rather than another snippet on the message, because the two answer different questions. A snippet
/// is cut from what somebody typed into the message and needs no coordinate beyond the message itself; this is cut from
/// a file, and a reader who cannot be told which file and which page of it has been shown words they cannot go and
/// check. Folding one into the other would also make a message quote text its body never carried.
/// </para>
/// <para>
/// <see cref="Kind" /> is what separates the two things an attachment can contribute. A document's own words were
/// written by whoever sent the file and compete as an ordinary match; a description was composed by a model out of a
/// picture, and a result carrying one is in the list only because nothing anybody wrote was near the query.
/// </para>
/// <para>
/// <see cref="Extracts" /> is bounded mail content and inherits the classification of the message it hangs on — twice
/// over where the kind is a description, that text being a machine's account of mail content. Nothing here is written to
/// a log, a metric, a trace, or an error message, and no member of it is a whole attachment: what a match publishes is
/// what a snippet publishes, cut to the same deployment bounds.
/// </para>
/// </remarks>
/// <param name="AttachmentPosition">The zero-based walk position of the attachment, which is the coordinate the download route is addressed with.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="DeclaredMediaType">What the part declared itself to be, which is the sender's claim rather than a reading of the octets.</param>
/// <param name="Kind">Whether the words are the file's own or a model's account of a picture.</param>
/// <param name="Segment">The page, slide, or sheet the extract was read from, or <see langword="null" /> where the reading recorded no boundaries.</param>
/// <param name="Extracts">The bounded extracts themselves, in the order the attachment's text carries them.</param>
public sealed record EmailAttachmentMatch(
    int AttachmentPosition,
    string? FileName,
    string DeclaredMediaType,
    AttachmentTextKind Kind,
    AttachmentTextSegment? Segment,
    IReadOnlyList<string> Extracts);

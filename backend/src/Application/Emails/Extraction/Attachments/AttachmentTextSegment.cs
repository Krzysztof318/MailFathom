// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Where one page, slide, or sheet of a document begins in the text the whole document yielded.</summary>
/// <remarks>
/// <para>
/// The reason the extraction records this at all is that a passage cut out of a document has to say where in the
/// document it came from, and the offsets a chunk carries index the extracted text rather than the file. The segment
/// list is what turns the second into the first: the last segment beginning at or before a passage's start offset is
/// the place that passage was read from, which is what a citation opens.
/// </para>
/// <para>
/// Recording the boundaries at extraction rather than re-deriving them later is what keeps re-cutting a mailbox a local
/// cost. A boundary rule tuned upwards re-reads the stored segments and re-cuts against them; re-deriving would mean
/// parsing every attachment again, which is the one cost
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0029-what-an-embedding-is-derived-from-and-whether-attachment-text-joins-it.md">ADR 0029</see>
/// separated from the embedding budget precisely because it is the expensive one.
/// </para>
/// <para>
/// A segment carries no characters. <see cref="Label" /> is the one member read out of the document, and it is a sheet
/// name a sender chose — so it is mail content and is treated as such wherever it is stored or shown.
/// </para>
/// </remarks>
/// <param name="Kind">What the segment is, in the word its own format uses.</param>
/// <param name="Number">The segment's one-based place among the segments of its own kind, in reading order.</param>
/// <param name="Label">The name the format records for it, or <see langword="null" /> where the format records none.</param>
/// <param name="StartOffset">Where the segment's text begins in the document's extracted text.</param>
public sealed record AttachmentTextSegment(
    AttachmentTextSegmentKind Kind,
    int Number,
    string? Label,
    int StartOffset)
{
    /// <summary>Finds the place a passage beginning at an offset was read from.</summary>
    /// <param name="segments">One attachment's boundaries, in the ascending order the reading wrote them.</param>
    /// <param name="startOffset">Where the passage begins in that attachment's extracted text.</param>
    /// <returns>The place, or <see langword="null" /> where the attachment records none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segments" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The last boundary at or before the offset rather than the first at or after it: a passage opening halfway down
    /// page four was read from page four, and citing page five would send a reader past the words they were looking
    /// for. An attachment carrying no boundaries answers with nothing, which is a citation naming the file and not a
    /// place inside it — what an honest reading of a row written before boundaries existed, or one whose redaction
    /// changed the length of its text, supports.
    /// </remarks>
    public static AttachmentTextSegment? At(IReadOnlyList<AttachmentTextSegment> segments, int startOffset)
    {
        ArgumentNullException.ThrowIfNull(segments);

        return segments.TakeWhile(segment => segment.StartOffset <= startOffset).LastOrDefault();
    }
}

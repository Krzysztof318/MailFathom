// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads one message's attachment passages and turns each one's offset into the page it was read from.</summary>
/// <remarks>
/// Two projections joined in this process rather than one query joining the tables, because the boundaries are a
/// document per attachment while the passages are a row each: a join would return the whole boundary list once per
/// passage, which for a long report is the list repeated a few thousand times over the wire. Read separately, each
/// attachment's boundaries cross once — and only for the attachments the requested window actually reached.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailAttachmentPassageReader(MailFathomDbContext dbContext) : IEmailAttachmentPassageReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AttachmentPassage>> ReadAttachmentPassagesAsync(
        StoredEmailId emailId,
        AttachmentPassagePosition? resumeAfter,
        int windowSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize);

        var afterAttachment = resumeAfter?.AttachmentPosition;
        var afterOrdinal = resumeAfter?.Ordinal;

        // The window is taken over the passages, and the attachment rows are then read for the positions this window
        // actually reached. Reading them first would load every attachment's boundary document of a message the window
        // covers one page of, which is the amplification the bound exists to stop.
        var passages = await dbContext.EmailChunks
            .AsNoTracking()
            .Where(chunk => chunk.StoredEmailId == emailId.Value
                && chunk.AttachmentPosition != null
                && (afterAttachment == null
                    || chunk.AttachmentPosition > afterAttachment
                    || (chunk.AttachmentPosition == afterAttachment && chunk.Ordinal > afterOrdinal)))
            .OrderBy(chunk => chunk.AttachmentPosition)
            .ThenBy(chunk => chunk.Ordinal)
            .Take(windowSize)
            .Select(chunk => new PassageRow(
                chunk.AttachmentPosition!.Value,
                chunk.Ordinal,
                chunk.StartOffset,
                chunk.Text))
            .ToArrayAsync(cancellationToken);

        if (passages.Length == 0)
        {
            return [];
        }

        var positions = passages.Select(passage => passage.AttachmentPosition).Distinct().ToArray();

        var attachments = await dbContext.EmailAttachmentTexts
            .AsNoTracking()
            .Where(text => text.StoredEmailId == emailId.Value && positions.Contains(text.AttachmentPosition))
            .Select(text => new AttachmentRow(
                text.AttachmentPosition,
                text.FileName,
                text.DeclaredMediaType,
                text.Kind,
                text.Segments))
            .ToDictionaryAsync(row => row.Position, cancellationToken);

        var boundaries = attachments.ToDictionary(
            entry => entry.Key,
            entry => DeserializeSegments(entry.Value.Segments));

        return
        [
            // A passage whose attachment row is gone is dropped rather than published without one: the two were written
            // in one statement, so the only way to see that is a deletion racing this read, and a passage of a file
            // this message no longer records is not something to hand a reader.
            .. passages
                .Where(passage => attachments.ContainsKey(passage.AttachmentPosition))
                .Select(passage => Compose(
                    passage,
                    attachments[passage.AttachmentPosition],
                    boundaries[passage.AttachmentPosition])),
        ];
    }

    /// <summary>Builds one published passage from its row, its attachment, and that attachment's boundaries.</summary>
    private static AttachmentPassage Compose(
        PassageRow passage,
        AttachmentRow attachment,
        IReadOnlyList<AttachmentTextSegment> segments) => new(
        passage.AttachmentPosition,
        passage.Ordinal,
        attachment.FileName,
        attachment.DeclaredMediaType,
        attachment.Kind,
        AttachmentTextSegment.At(segments, passage.StartOffset),
        passage.Text);

    /// <summary>Reads back the boundary document one attachment's row stores.</summary>
    /// <remarks>
    /// A row written before this feature stored boundaries carries none, which is not an error: the passages are still
    /// passages, and a citation without a page is what an honest reading of them supports. A redaction that rewrote the
    /// text to a different length is not one of those rows — <c>DerivedAttachmentText.WithRedactedText</c> moves every
    /// boundary through <c>RedactedText.MapOffset</c> before the document is serialized, so what a redacted attachment
    /// loses is only the boundaries the scan's analyzed ceiling cut the text short of.
    /// </remarks>
    private static IReadOnlyList<AttachmentTextSegment> DeserializeSegments(string? document) => document is null
        ? []
        : JsonSerializer.Deserialize(
            document,
            AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment) ?? [];

    /// <summary>One attachment's identity and boundaries, as the projection returns them.</summary>
    private sealed record AttachmentRow(
        int Position,
        string? FileName,
        string DeclaredMediaType,
        AttachmentTextKind Kind,
        string? Segments);

    /// <summary>One passage of an attachment, as the projection returns it.</summary>
    private sealed record PassageRow(int AttachmentPosition, int Ordinal, int StartOffset, string Text);
}

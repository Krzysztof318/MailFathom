// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads a plain-text, Markdown, or delimiter-separated attachment as the characters it was written with.</summary>
/// <remarks>
/// <para>
/// This is the one family whose octets already are the text, so there is no parser here and none is wanted. Nothing is
/// rendered, no markup is interpreted, no HTML is parsed out of a Markdown file, no field is split out of a delimited
/// row, and no link or image reference is resolved or fetched: a heading marker, a link's target, and a comma between
/// two cells are as searchable as the prose around them, so stripping any of them would lose characters a person typed
/// and would put this reader in the business of deciding what a file means. That is also what keeps it clear of
/// everything an HTML attachment would drag in, which is why no such format reaches it.
/// </para>
/// <para>
/// The octets still get no benefit of the doubt, a sender having written both the media type and the file name. This
/// decodes rather than parses, and it decodes strictly: a byte-order mark decides the encoding, everything else is
/// UTF-8, and octets that are not text raise rather than arriving as a page of replacement characters that would be
/// indexed as if somebody had written them. A NUL is refused on the same rule and needs saying separately, because it
/// decodes cleanly — it is the one shape of binary a strict decoder would let through.
/// </para>
/// </remarks>
internal sealed class PlainTextAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    /// <summary>The characters decoded between two readings of the deadline and the output ceiling.</summary>
    private const int BlockCharacters = 4096;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The byte-order marks recognized, longest first so a UTF-32 mark is not read as its UTF-16 prefix.</summary>
    private static readonly (byte[] Mark, Encoding Encoding)[] ByteOrderMarks =
    [
        ([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true)),
        ([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true)),
        ([0xEF, 0xBB, 0xBF], Utf8),
        ([0xFF, 0xFE], new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true)),
        ([0xFE, 0xFF], new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true)),
    ];

    /// <summary>Reads one text attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="cancellationToken">Cancels the read between blocks of characters.</param>
    /// <returns>What the file yielded, as one page.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the output ceiling is crossed.</exception>
    /// <exception cref="DecoderFallbackException">Thrown when the octets do not decode under the encoding they declared.</exception>
    /// <exception cref="InvalidDataException">Thrown when the decoded characters carry a NUL, which text does not.</exception>
    public ExtractedAttachmentText Read(Stream content, CancellationToken cancellationToken)
    {
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);

        using var reader = new StreamReader(
            content,
            EncodingOf(content),
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var block = new char[BlockCharacters];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = reader.Read(block, 0, block.Length);

            if (read == 0)
            {
                break;
            }

            var characters = new string(block, 0, read);

            if (characters.Contains('\0', StringComparison.Ordinal))
            {
                throw new InvalidDataException("The octets carry a NUL, so they are not the text they declare.");
            }

            text.Add(characters);
        }

        // Read back as gathered rather than through the trim, which exists for the readers that close a page with
        // a break of their own: nothing here inserts one, so a file ending in a newline ends in a newline.
        var extracted = text.ToTextAsGathered();

        // One page, matching the answer a word-processing document already gives, so a citation into a text file
        // resolves through the same coordinate scheme as one into any other document.
        return new ExtractedAttachmentText(
            extracted,
            PageCount: 1,
            string.IsNullOrWhiteSpace(extracted) ? [1] : [],
            [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, Number: 1, Label: null, StartOffset: 0)]);
    }

    /// <summary>Reads the byte-order mark the file opens with, and leaves the stream positioned past it.</summary>
    /// <remarks>
    /// The mark is the only thing about the encoding this trusts, because it is the only statement the file itself
    /// makes: the media type reaching recognition carries no charset parameter, a sender's file name says nothing, and
    /// guessing a legacy code page from the octets would turn a refusal into a page of plausible wrong characters.
    /// </remarks>
    private static Encoding EncodingOf(Stream content)
    {
        Span<byte> opening = stackalloc byte[4];

        var read = content.ReadAtLeast(opening, opening.Length, throwOnEndOfStream: false);

        foreach (var (mark, encoding) in ByteOrderMarks)
        {
            if (read >= mark.Length && opening[..mark.Length].SequenceEqual(mark))
            {
                content.Position = mark.Length;

                return encoding;
            }
        }

        content.Position = 0;

        return Utf8;
    }
}

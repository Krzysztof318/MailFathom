// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Application.Emails.Extraction.Images;

/// <summary>What an image's own header says it is and how large a grid it declares.</summary>
/// <param name="Format">The format the octets are in, decided from the octets rather than from any declared media type.</param>
/// <param name="Width">The declared width in pixels.</param>
/// <param name="Height">The declared height in pixels.</param>
/// <remarks>
/// <para>
/// This is a read of a few octets and never a decode. Nothing here allocates for a pixel grid, expands a compressed
/// stream, or hands the octets to a codec, which is what makes it safe to run on an attachment a stranger composed: a
/// file declaring a grid of billions of pixels costs the same few comparisons as one declaring a thumbnail, and it is
/// refused on the declaration rather than on the allocation the declaration would have caused.
/// </para>
/// <para>
/// The grid is what the file declares. Nothing here proves the compressed data behind it matches, because proving that
/// is the decode this exists to avoid; a file whose header understates its content is refused by whatever decodes it
/// next, having never been sent under a bound this deployment believed.
/// </para>
/// </remarks>
public sealed record ImageAttachmentHeader(ImageAttachmentFormat Format, int Width, int Height)
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8];

    private static ReadOnlySpan<byte> WebpKeyFrameStartCode => [0x9D, 0x01, 0x2A];

    private static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> MarkupLeadingWhitespace => [(byte)' ', (byte)'\t', (byte)'\r', (byte)'\n'];

    /// <summary>Gets how many pixels the declared grid holds.</summary>
    /// <remarks>Computed in 64 bits because two dimensions a header is free to state as large as it likes multiply past what 32 bits hold, and an overflow here would turn the bomb bound into one that admits the bomb.</remarks>
    public long PixelCount => (long)this.Width * this.Height;

    /// <summary>Gets the media type the octets are actually in, which is what a request states rather than what the part declared.</summary>
    /// <remarks>The declared media type is the sender's and may say anything; this one is read from the octets, so a provider is told what it is about to decode.</remarks>
    public string MediaType => this.Format switch
    {
        ImageAttachmentFormat.Png => "image/png",
        ImageAttachmentFormat.Jpeg => "image/jpeg",
        ImageAttachmentFormat.Webp => "image/webp",
        _ => "image/gif",
    };

    /// <summary>Reads what the octets declare themselves to be, or names why they will not be described.</summary>
    /// <param name="content">The attachment's decoded octets, whole.</param>
    /// <param name="header">What the octets declare, when they declare a supported format readably.</param>
    /// <param name="refusal">Why no header was read, meaningful only when this returns <see langword="false" />.</param>
    /// <returns><see langword="true" /> when a header was read, <see langword="false" /> when the octets are refused.</returns>
    /// <remarks>
    /// A markup document is separated from an unrecognized format before any signature is tried, because the two are
    /// refused for different reasons and an SVG arriving as an unrecognized format would say nothing about why it will
    /// never be admitted.
    /// </remarks>
    public static bool TryRead(
        ReadOnlySpan<byte> content,
        [NotNullWhen(true)] out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        header = null;

        if (IsMarkupDocument(content))
        {
            refusal = ImageDescriptionRefusal.FormatExcluded;

            return false;
        }

        if (content.StartsWith(PngSignature))
        {
            return TryReadPng(content, out header, out refusal);
        }

        if (content.StartsWith(JpegSignature))
        {
            return TryReadJpeg(content, out header, out refusal);
        }

        if (content.StartsWith("GIF8"u8))
        {
            return TryReadGif(content, out header, out refusal);
        }

        if (content.StartsWith("RIFF"u8) && content.Length >= 12 && content[8..].StartsWith("WEBP"u8))
        {
            return TryReadWebp(content, out header, out refusal);
        }

        refusal = ImageDescriptionRefusal.FormatNotSupported;

        return false;
    }

    /// <summary>Reports whether the octets open as a markup document rather than as any raster image.</summary>
    /// <remarks>
    /// A leading <c>&lt;</c>, past a byte-order mark and any leading whitespace, is what every SVG, every XML wrapper
    /// around one, and every HTML document sent as an image begins with, and is what no format on the allow-list ever
    /// begins with. Testing the shape rather than a list of root elements is what keeps a comment, a processing
    /// instruction, or a doctype in front of <c>&lt;svg&gt;</c> from walking past the exclusion.
    /// </remarks>
    private static bool IsMarkupDocument(ReadOnlySpan<byte> content)
    {
        var rest = content.StartsWith(Utf8ByteOrderMark) ? content[Utf8ByteOrderMark.Length..] : content;
        var opening = rest.TrimStart(MarkupLeadingWhitespace);

        return opening.Length > 0 && opening[0] == (byte)'<';
    }

    private static bool TryReadPng(
        ReadOnlySpan<byte> content,
        out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        // The signature is followed by the length and the type of the first chunk, which the format requires to be
        // IHDR, and the grid is the first eight octets of that chunk's data.
        if (content.Length < 24 || !content[12..].StartsWith("IHDR"u8))
        {
            return Unreadable(out header, out refusal);
        }

        return Published(
            (int)BinaryPrimitives.ReadUInt32BigEndian(content[16..]),
            (int)BinaryPrimitives.ReadUInt32BigEndian(content[20..]),
            ImageAttachmentFormat.Png,
            out header,
            out refusal);
    }

    /// <summary>Walks the segment chain to whichever start-of-frame marker states the grid.</summary>
    /// <remarks>
    /// The grid is not at a fixed offset in a JPEG: an EXIF block, a colour profile, or an embedded thumbnail sits in
    /// front of it and each is as long as it says it is. The walk reads segment lengths and stops at the first frame
    /// header, so it costs one pass over the segment table rather than a decode, and a chain that runs off the end of
    /// the file is a truncated image rather than an unsupported one.
    /// </remarks>
    private static bool TryReadJpeg(
        ReadOnlySpan<byte> content,
        out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        var position = JpegSignature.Length;

        while (position + 1 < content.Length)
        {
            if (content[position] != 0xFF)
            {
                return Unreadable(out header, out refusal);
            }

            var marker = content[position + 1];

            // Fill octets before a marker, and the standalone markers, which carry neither a length nor a payload.
            if (marker is 0xFF)
            {
                position++;
                continue;
            }

            if (marker is 0x01 or 0xD8 || marker is >= 0xD0 and <= 0xD7)
            {
                position += 2;
                continue;
            }

            if (position + 4 > content.Length)
            {
                return Unreadable(out header, out refusal);
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content[(position + 2)..]);

            if (segmentLength < 2)
            {
                return Unreadable(out header, out refusal);
            }

            // Every start-of-frame marker states the grid the same way, whatever coding it introduces. The three
            // excluded from the run share the numbering and carry no frame header: the Huffman table, the arithmetic
            // conditioning table, and the restart interval.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (position + 9 > content.Length)
                {
                    return Unreadable(out header, out refusal);
                }

                return Published(
                    BinaryPrimitives.ReadUInt16BigEndian(content[(position + 7)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(content[(position + 5)..]),
                    ImageAttachmentFormat.Jpeg,
                    out header,
                    out refusal);
            }

            position += 2 + segmentLength;
        }

        return Unreadable(out header, out refusal);
    }

    private static bool TryReadGif(
        ReadOnlySpan<byte> content,
        out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        if (content.Length < 10)
        {
            return Unreadable(out header, out refusal);
        }

        return Published(
            BinaryPrimitives.ReadUInt16LittleEndian(content[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(content[8..]),
            ImageAttachmentFormat.Gif,
            out header,
            out refusal);
    }

    /// <summary>Reads the grid out of whichever of the three WebP chunk layouts the file uses.</summary>
    /// <remarks>
    /// The three are a different format each sharing one container: lossy carries a VP8 key-frame header, lossless
    /// packs two fourteen-bit dimensions into a bit field, and extended states a canvas the frames inside it are
    /// composed onto. Both of the last two state each dimension one less than it is, which is the encoding rather than
    /// an adjustment made here.
    /// </remarks>
    private static bool TryReadWebp(
        ReadOnlySpan<byte> content,
        out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        if (content.Length >= 30 && content[12..].StartsWith("VP8 "u8)
            && content[23..].StartsWith(WebpKeyFrameStartCode))
        {
            return Published(
                BinaryPrimitives.ReadUInt16LittleEndian(content[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(content[28..]) & 0x3FFF,
                ImageAttachmentFormat.Webp,
                out header,
                out refusal);
        }

        if (content.Length >= 25 && content[12..].StartsWith("VP8L"u8) && content[20] == 0x2F)
        {
            var packed = BinaryPrimitives.ReadUInt32LittleEndian(content[21..]);

            return Published(
                (int)(packed & 0x3FFF) + 1,
                (int)((packed >> 14) & 0x3FFF) + 1,
                ImageAttachmentFormat.Webp,
                out header,
                out refusal);
        }

        if (content.Length >= 30 && content[12..].StartsWith("VP8X"u8))
        {
            return Published(
                ReadUInt24LittleEndian(content[24..]) + 1,
                ReadUInt24LittleEndian(content[27..]) + 1,
                ImageAttachmentFormat.Webp,
                out header,
                out refusal);
        }

        return Unreadable(out header, out refusal);
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> content) =>
        content[0] | (content[1] << 8) | (content[2] << 16);

    /// <summary>Publishes a grid, or refuses one no image has.</summary>
    /// <remarks>
    /// A dimension of zero, and one a format allowed to state in thirty-two bits wrote past what a signed integer
    /// holds, are both refused here rather than carried into the pixel-count bound, where the first would pass every
    /// ceiling and the second would arrive negative and do the same.
    /// </remarks>
    private static bool Published(
        int width,
        int height,
        ImageAttachmentFormat format,
        out ImageAttachmentHeader? header,
        out ImageDescriptionRefusal refusal)
    {
        if (width <= 0 || height <= 0)
        {
            return Unreadable(out header, out refusal);
        }

        header = new ImageAttachmentHeader(format, width, height);
        refusal = default;

        return true;
    }

    private static bool Unreadable(out ImageAttachmentHeader? header, out ImageDescriptionRefusal refusal)
    {
        header = null;
        refusal = ImageDescriptionRefusal.ImageUnreadable;

        return false;
    }
}

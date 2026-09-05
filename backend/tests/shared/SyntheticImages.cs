// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;

namespace MailFathom.TestSupport;

/// <summary>Builds the smallest file of each format that carries a readable header, so a test states only the grid it varies.</summary>
/// <remarks>
/// <para>
/// Headers rather than pictures. Nothing under test decodes an image, so what a test needs is the octets a decoder
/// would read the grid out of and nothing behind them — which is also what lets a "billion-pixel" file be forty octets
/// long, exactly as the decompression bomb this bounds would be.
/// </para>
/// <para>
/// Every builder but <see cref="Png" /> refuses a dimension its format cannot state, rather than writing whatever the
/// low bits come to. Truncation here would be the worst kind of green: a caller asking for a hundred thousand pixels
/// square would get a GIF declaring 34464 and a lossy WebP declaring 1632, so a decompression-bomb assertion written
/// that way would pass while proving the opposite of what it names. <see cref="Png" /> guards nothing because its field
/// is a full thirty-two bits, which is what lets a test hand it <see cref="int.MinValue" /> to state a width past what
/// a signed integer holds.
/// </para>
/// </remarks>
internal static class SyntheticImages
{
    /// <summary>Builds a PNG declaring the grid, with the IHDR chunk the format requires first.</summary>
    public static byte[] Png(int width, int height)
    {
        var file = new byte[24];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(file);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), 13);
        "IHDR"u8.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)height);

        return file;
    }

    /// <summary>Builds a JPEG whose start-of-frame marker sits behind an application segment of the given length.</summary>
    /// <remarks>The padding segment is what makes the walk worth testing: a reader that looked at a fixed offset would pass on a bare frame header and fail on every photograph a camera writes.</remarks>
    public static byte[] Jpeg(int width, int height, int precedingSegmentPayload = 0)
    {
        RequireSixteenBits(width, height);

        List<byte> file = [0xFF, 0xD8];

        if (precedingSegmentPayload > 0)
        {
            var segmentLength = precedingSegmentPayload + 2;

            file.AddRange([0xFF, 0xE1, (byte)(segmentLength >> 8), (byte)(segmentLength & 0xFF)]);
            file.AddRange(new byte[precedingSegmentPayload]);
        }

        file.AddRange([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        file.AddRange([(byte)(height >> 8), (byte)(height & 0xFF)]);
        file.AddRange([(byte)(width >> 8), (byte)(width & 0xFF)]);

        return [.. file];
    }

    /// <summary>Builds a GIF whose logical screen descriptor declares the grid.</summary>
    public static byte[] Gif(int width, int height)
    {
        RequireSixteenBits(width, height);

        var file = new byte[13];

        "GIF89a"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(8), (ushort)height);

        return file;
    }

    /// <summary>Builds a lossy WebP, whose grid sits in the VP8 key-frame header behind the start code.</summary>
    public static byte[] LossyWebp(int width, int height)
    {
        // The two bitstreams do not share a ceiling: this one states the dimension itself in fourteen bits, while
        // lossless below states it one less than it is and so reaches one higher.
        RequireDimensions(width, height, greatest: 0x3FFF);

        var file = WebpContainer("VP8 "u8, contentLength: 18);

        file[23] = 0x9D;
        file[24] = 0x01;
        file[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), (ushort)height);

        return file;
    }

    /// <summary>Builds a lossless WebP, whose two dimensions are packed one less than they are into fourteen bits each.</summary>
    public static byte[] LosslessWebp(int width, int height)
    {
        RequireFourteenBits(width, height);

        var file = WebpContainer("VP8L"u8, contentLength: 13);

        file[20] = 0x2F;
        BinaryPrimitives.WriteUInt32LittleEndian(
            file.AsSpan(21),
            (uint)((width - 1) & 0x3FFF) | ((uint)((height - 1) & 0x3FFF) << 14));

        return file;
    }

    /// <summary>Builds an extended WebP, whose canvas states each dimension one less than it is in twenty-four bits.</summary>
    public static byte[] ExtendedWebp(int width, int height)
    {
        RequireDimensions(width, height, greatest: 0xFFFFFF);

        var file = WebpContainer("VP8X"u8, contentLength: 18);

        WriteUInt24LittleEndian(file.AsSpan(24), width - 1);
        WriteUInt24LittleEndian(file.AsSpan(27), height - 1);

        return file;
    }

    /// <summary>Refuses a grid a sixteen-bit field cannot state.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either dimension is not between 1 and 65535.</exception>
    private static void RequireSixteenBits(int width, int height) =>
        RequireDimensions(width, height, ushort.MaxValue);

    /// <summary>Refuses a grid the lossless WebP bitstream cannot state, which packs each dimension one less than it is into fourteen bits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either dimension is not between 1 and 16384.</exception>
    private static void RequireFourteenBits(int width, int height) =>
        RequireDimensions(width, height, 1 << 14);

    private static void RequireDimensions(int width, int height, int greatest)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, greatest);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, greatest);
    }

    private static byte[] WebpContainer(ReadOnlySpan<byte> chunk, int contentLength)
    {
        var file = new byte[20 + contentLength];

        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(file.Length - 8));
        "WEBP"u8.CopyTo(file.AsSpan(8));
        chunk.CopyTo(file.AsSpan(12));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)contentLength);

        return file;
    }

    private static void WriteUInt24LittleEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)(value & 0xFF);
        destination[1] = (byte)((value >> 8) & 0xFF);
        destination[2] = (byte)((value >> 16) & 0xFF);
    }
}

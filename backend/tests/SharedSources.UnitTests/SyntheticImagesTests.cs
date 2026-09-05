// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Binary;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the octets each builder emits, independently of anything that reads them.</summary>
/// <remarks>
/// Two suites read these files to state what an image header parser does, so a builder writing a dimension at the wrong
/// offset or in the wrong byte order would agree with a parser making the same mistake and both would pass. These
/// assertions are against the format's own layout rather than against a reader, which is what makes them able to
/// disagree.
/// </remarks>
public sealed class SyntheticImagesTests
{
    [Fact]
    public void Png_AGrid_WritesTheSignatureAndTheImageHeaderChunkTheFormatRequiresFirst()
    {
        // Act
        var file = SyntheticImages.Png(width: 640, height: 480);

        // Assert
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], file[..8]);
        Assert.Equal("IHDR"u8.ToArray(), file[12..16]);
        Assert.Equal(640u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(16)));
        Assert.Equal(480u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(20)));
    }

    /// <summary>A dimension past what a signed integer holds is written as the unsigned value the format states, which is the input the pixel ceiling is proved against.</summary>
    [Fact]
    public void Png_AWidthPastASignedInteger_WritesTheUnsignedValueTheFormatStates()
    {
        // Act
        var file = SyntheticImages.Png(width: int.MinValue, height: 8);

        // Assert
        Assert.Equal(0x80000000u, BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(16)));
    }

    /// <summary>The frame header moves with the padding segment in front of it, which is the whole reason a walk is being tested rather than a fixed offset.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(4096)]
    public void Jpeg_APaddingSegmentOfAnyLength_PutsTheGridBehindTheStartOfFrameMarker(int precedingSegmentPayload)
    {
        // Act
        var file = SyntheticImages.Jpeg(width: 640, height: 480, precedingSegmentPayload);

        // Assert
        Assert.Equal([0xFF, 0xD8], file[..2]);

        var frame = file.Length - 9;

        Assert.Equal(0xFF, file[frame]);
        Assert.Equal(0xC0, file[frame + 1]);
        Assert.Equal(480, BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(frame + 5)));
        Assert.Equal(640, BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(frame + 7)));
    }

    [Fact]
    public void Gif_AGrid_WritesTheLogicalScreenDescriptorInLittleEndianBehindTheSignature()
    {
        // Act
        var file = SyntheticImages.Gif(width: 640, height: 480);

        // Assert
        Assert.Equal("GIF89a"u8.ToArray(), file[..6]);
        Assert.Equal(640, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6)));
        Assert.Equal(480, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(8)));
    }

    /// <summary>Every WebP layout is one RIFF container naming WEBP and then its own chunk, which is what a reader keys on before it reads a dimension at all.</summary>
    [Theory]
    [InlineData("VP8 ")]
    [InlineData("VP8L")]
    [InlineData("VP8X")]
    public void Webp_EachLayout_NamesItsChunkInsideARiffContainerDeclaringItsOwnLength(string chunk)
    {
        // Act
        var file = WebpOf(chunk);

        // Assert
        Assert.Equal("RIFF"u8.ToArray(), file[..4]);
        Assert.Equal("WEBP"u8.ToArray(), file[8..12]);
        Assert.Equal(chunk, System.Text.Encoding.ASCII.GetString(file, 12, 4));
        Assert.Equal((uint)(file.Length - 8), BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
    }

    /// <summary>The key frame states the grid in fourteen bits each, behind the three-octet start code the format puts in front of it.</summary>
    [Fact]
    public void LossyWebp_AGrid_WritesItBehindTheKeyFrameStartCode()
    {
        // Act
        var file = SyntheticImages.LossyWebp(width: 640, height: 480);

        // Assert
        Assert.Equal([0x9D, 0x01, 0x2A], file[23..26]);
        Assert.Equal(640, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(26)) & 0x3FFF);
        Assert.Equal(480, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(28)) & 0x3FFF);
    }

    /// <summary>Lossless packs both dimensions one less than they are, which is the off-by-one a builder and a reader could agree on and both be wrong.</summary>
    [Fact]
    public void LosslessWebp_AGrid_PacksBothDimensionsOneLessThanTheyAre()
    {
        // Act
        var file = SyntheticImages.LosslessWebp(width: 640, height: 480);

        // Assert
        Assert.Equal(0x2F, file[20]);

        var packed = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(21));

        Assert.Equal(639u, packed & 0x3FFF);
        Assert.Equal(479u, (packed >> 14) & 0x3FFF);
    }

    /// <summary>The extended canvas states each dimension one less than it is, in twenty-four bits rather than fourteen.</summary>
    [Fact]
    public void ExtendedWebp_AGrid_StatesTheCanvasOneLessThanItIsInTwentyFourBits()
    {
        // Act
        var file = SyntheticImages.ExtendedWebp(width: 640, height: 480);

        // Assert
        Assert.Equal(639, ReadUInt24LittleEndian(file.AsSpan(24)));
        Assert.Equal(479, ReadUInt24LittleEndian(file.AsSpan(27)));
    }

    private static byte[] WebpOf(string chunk) => chunk switch
    {
        "VP8 " => SyntheticImages.LossyWebp(width: 8, height: 8),
        "VP8L" => SyntheticImages.LosslessWebp(width: 8, height: 8),
        _ => SyntheticImages.ExtendedWebp(width: 8, height: 8),
    };

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> content) =>
        content[0] | (content[1] << 8) | (content[2] << 16);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Emails.Extraction.Images;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction.Images;

/// <summary>Covers what an image declares about itself, which is the whole of what stands between an attachment and a decoder.</summary>
public sealed class ImageAttachmentHeaderTests
{
    private const int RealWidth = 37;

    private const int RealHeight = 19;

    /// <summary>Real files of each format, written by an encoder nobody here wrote and carried as base64.</summary>
    /// <remarks>
    /// The synthetic builders produce the same layouts, so a reader tested against them alone would agree with whatever
    /// misunderstanding the builders share. These are what ground it: files carrying the metadata, colour profile, and
    /// padding a real encoder emits, all of them stating one grid.
    /// </remarks>
    private const string RealPng =
        "iVBORw0KGgoAAAANSUhEUgAAACUAAAATAQMAAAAtc1bwAAAAIGNIUk0AAHomAACAhAAA+gAAAIDoAAB1MAAA6mAAADqYAAAXcJy6UTwAAAAGUExURf8AAP///0EdNBEAAAABYktHRAH/Ai3eAAAAB3RJTUUH6gkFEiksKPmEEQAAACV0RVh0ZGF0ZTpjcmVhdGUAMjAyNi0wOS0wNVQxODo0MTo0NCswMDowMDJlfPcAAAAldEVYdGRhdGU6bW9kaWZ5ADIwMjYtMDktMDVUMTg6NDE6NDQrMDA6MDBDOMRLAAAAKHRFWHRkYXRlOnRpbWVzdGFtcAAyMDI2LTA5LTA1VDE4OjQxOjQ0KzAwOjAwFC3llAAAAAxJREFUCNdjYKA3AAAAcgAB65yj5gAAAABJRU5ErkJggg==";

    private const string RealBaselineJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAATACUDAREAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAcJ/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AnRDGqYAAAAAAAAAAAAAD/9k=";

    private const string RealProgressiveJpeg =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wgARCAATACUDAREAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xAAWAQEBAQAAAAAAAAAAAAAAAAAABgj/2gAMAwEAAhADEAAAAZzC6pAAAAAAA//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAADD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAADD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAADD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQ/wD/AP8A/wD/AP8A/8QAFBEBAAAAAAAAAAAAAAAAAAAAMP/aAAgBAwEBPxB//8QAFBEBAAAAAAAAAAAAAAAAAAAAMP/aAAgBAgEBPxB//8QAFBABAAAAAAAAAAAAAAAAAAAAMP/aAAgBAQABPxB//9k=";

    private const string RealGif =
        "R0lGODlhJQATAPAAAP8AAAAAACH5BAAAAAAALAAAAAAlABMAAAIYhI+py+0Po5y02ouz3rz7D4biSJbmiaYFADs=";

    private const string RealLossyWebp =
        "UklGRk4AAABXRUJQVlA4IEIAAABQAwCdASolABMAPpFGnkslo6KhpWgAsBIJZwDO3oAAK/fDcAD+7qY//2LOWwLx//7nA/7nA/7nA/jbB+29aoAAAAA=";

    private const string RealLosslessWebp = "UklGRhwAAABXRUJQVlA4TA8AAAAvJIAEAAcQ/Y/+ByKi/wEA";

    public static TheoryData<string, ImageAttachmentFormat> RealFiles => new()
    {
        { RealPng, ImageAttachmentFormat.Png },
        { RealBaselineJpeg, ImageAttachmentFormat.Jpeg },
        { RealProgressiveJpeg, ImageAttachmentFormat.Jpeg },
        { RealGif, ImageAttachmentFormat.Gif },
        { RealLossyWebp, ImageAttachmentFormat.Webp },
        { RealLosslessWebp, ImageAttachmentFormat.Webp },
    };

    public static TheoryData<byte[], ImageAttachmentFormat> SyntheticFiles => new()
    {
        { SyntheticImages.Png(width: 640, height: 480), ImageAttachmentFormat.Png },
        { SyntheticImages.Jpeg(width: 640, height: 480), ImageAttachmentFormat.Jpeg },
        { SyntheticImages.Jpeg(width: 640, height: 480, precedingSegmentPayload: 4096), ImageAttachmentFormat.Jpeg },
        { SyntheticImages.Gif(width: 640, height: 480), ImageAttachmentFormat.Gif },
        { SyntheticImages.LossyWebp(width: 640, height: 480), ImageAttachmentFormat.Webp },
        { SyntheticImages.LosslessWebp(width: 640, height: 480), ImageAttachmentFormat.Webp },
        { SyntheticImages.ExtendedWebp(width: 640, height: 480), ImageAttachmentFormat.Webp },
    };

    /// <summary>A file an encoder nobody here wrote is read as the format and the grid that encoder put in it.</summary>
    [Theory]
    [MemberData(nameof(RealFiles))]
    public void TryRead_AFileARealEncoderWrote_ReadsItsFormatAndGrid(string encoded, ImageAttachmentFormat format)
    {
        // Arrange
        var content = Convert.FromBase64String(encoded);

        // Act
        var read = ImageAttachmentHeader.TryRead(content, out var header, out _);

        // Assert
        Assert.True(read);
        Assert.NotNull(header);
        Assert.Equal(format, header.Format);
        Assert.Equal(RealWidth, header.Width);
        Assert.Equal(RealHeight, header.Height);
    }

    /// <summary>Every layout on the allow-list states a grid the reader reaches, including a JPEG whose frame header sits behind four kilobytes of metadata.</summary>
    [Theory]
    [MemberData(nameof(SyntheticFiles))]
    public void TryRead_AFileOfEachAdmittedLayout_ReadsItsFormatAndGrid(byte[] content, ImageAttachmentFormat format)
    {
        // Act
        var read = ImageAttachmentHeader.TryRead(content, out var header, out _);

        // Assert
        Assert.True(read);
        Assert.NotNull(header);
        Assert.Equal(format, header.Format);
        Assert.Equal(640, header.Width);
        Assert.Equal(480, header.Height);
    }

    /// <summary>A grid too wide for a signed multiplication is counted in 64 bits, because the bound is what it would otherwise overflow past.</summary>
    [Fact]
    public void PixelCount_AGridThatOverflowsThirtyTwoBits_IsCountedWithoutWrapping()
    {
        // Arrange
        var content = SyntheticImages.Png(width: 100_000, height: 100_000);

        // Act
        var read = ImageAttachmentHeader.TryRead(content, out var header, out _);

        // Assert
        Assert.True(read);
        Assert.NotNull(header);
        Assert.Equal(10_000_000_000L, header.PixelCount);
    }

    /// <summary>What a request states is read from the octets, never from what a part declared.</summary>
    [Theory]
    [InlineData(ImageAttachmentFormat.Png, "image/png")]
    [InlineData(ImageAttachmentFormat.Jpeg, "image/jpeg")]
    [InlineData(ImageAttachmentFormat.Webp, "image/webp")]
    [InlineData(ImageAttachmentFormat.Gif, "image/gif")]
    public void MediaType_EachAdmittedFormat_NamesWhatTheOctetsAre(ImageAttachmentFormat format, string mediaType)
    {
        // Arrange
        var header = new ImageAttachmentHeader(format, Width: 1, Height: 1);

        // Act, Assert
        Assert.Equal(mediaType, header.MediaType);
    }

    /// <summary>A markup document is refused as excluded rather than as unrecognized, past a byte-order mark, whitespace, and anything before the root element.</summary>
    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>")]
    [InlineData("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("\n  <!-- a comment first --><svg/>")]
    [InlineData("\uFEFF<!DOCTYPE svg><svg/>")]
    [InlineData("<html><body>not a picture</body></html>")]
    public void TryRead_AMarkupDocument_IsRefusedAsExcludedRatherThanUnrecognized(string document)
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes(document);

        // Act
        var read = ImageAttachmentHeader.TryRead(content, out _, out var refusal);

        // Assert
        Assert.False(read);
        Assert.Equal(ImageDescriptionRefusal.FormatExcluded, refusal);
    }

    /// <summary>A raster format nothing here reads is refused as unsupported, which is the reason that may one day change.</summary>
    [Fact]
    public void TryRead_AFormatOutsideTheAllowList_IsRefusedAsUnsupported()
    {
        // Arrange
        byte[] bitmap = [0x42, 0x4D, 0xDA, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x8A, 0x00];

        // Act
        var read = ImageAttachmentHeader.TryRead(bitmap, out _, out var refusal);

        // Assert
        Assert.False(read);
        Assert.Equal(ImageDescriptionRefusal.FormatNotSupported, refusal);
    }

    /// <summary>Octets that open as a supported format and then do not hold one are told apart from a format nothing reads.</summary>
    [Theory]
    [MemberData(nameof(CorruptFiles))]
    public void TryRead_ASupportedFormatThatDoesNotHoldOne_IsRefusedAsUnreadable(byte[] content)
    {
        // Act
        var read = ImageAttachmentHeader.TryRead(content, out _, out var refusal);

        // Assert
        Assert.False(read);
        Assert.Equal(ImageDescriptionRefusal.ImageUnreadable, refusal);
    }

    public static TheoryData<byte[]> CorruptFiles => new()
    {
        // A PNG signature and nothing behind it.
        SyntheticImages.Png(width: 8, height: 8)[..12],

        // A PNG whose first chunk is not the IHDR the format requires.
        PngWithoutImageHeader(),

        // A PNG declaring a grid with no pixels in it.
        SyntheticImages.Png(width: 8, height: 0),

        // A JPEG whose segment chain runs off the end before any frame header.
        SyntheticImages.Jpeg(width: 8, height: 8, precedingSegmentPayload: 64)[..20],

        // A JPEG whose segment chain leaves the marker alignment the format guarantees.
        new byte[] { 0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00 },

        // A GIF signature with no logical screen descriptor behind it.
        "GIF89a"u8.ToArray(),

        // A WebP container naming a chunk layout this reads none of.
        WebpWithAnUnknownChunk(),
    };

    private static byte[] PngWithoutImageHeader()
    {
        var file = SyntheticImages.Png(width: 8, height: 8);

        "IDAT"u8.CopyTo(file.AsSpan(12));

        return file;
    }

    private static byte[] WebpWithAnUnknownChunk()
    {
        var file = SyntheticImages.LossyWebp(width: 8, height: 8);

        "ANIM"u8.CopyTo(file.AsSpan(12));

        return file;
    }
}

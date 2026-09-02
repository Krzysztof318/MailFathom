// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Rendering.Document;

/// <summary>The octets a document carries in its own pictures, which is what an octet budget is spent against.</summary>
/// <remarks>
/// One arithmetic answers two questions — what a reduction may still emit, and what a read has left for the emails
/// after this one — so a defect here reports the wrong size to both. It is counted per occurrence rather than per
/// distinct source, because that is what the answer actually carries.
/// </remarks>
public sealed class MailDocumentImagesTests
{
    /// <summary>A composed <c>data:</c> URI is counted as the octets its encoding stands for, not as its own length.</summary>
    [Fact]
    public void OctetsBehind_AComposedDataUri_CountsWhatTheEncodingStandsFor()
    {
        // Arrange
        var source = $"data:image/png;base64,{Convert.ToBase64String(new byte[300])}";

        // Act
        var octets = MailDocumentImages.OctetsBehind(source);

        // Assert
        Assert.Equal(300, octets);
    }

    /// <summary>A source the document only points at carries nothing, because the answer carries the address alone.</summary>
    [Theory]
    [InlineData("https://sender.test/logo.png")]
    [InlineData("cid:one@example.test")]
    [InlineData("data:image/png;base64")]
    public void OctetsBehind_ASourceCarryingNoOctets_CountsNone(string source)
    {
        // Act, Assert
        Assert.Equal(0, MailDocumentImages.OctetsBehind(source));
    }

    /// <summary>The same picture drawn twice costs twice, because the document carries the whole encoding each time.</summary>
    /// <remarks>
    /// This is the difference between the bound on what was decoded and the bound on what the answer holds: one part
    /// resolves once, and a body naming it repeatedly composes a response many times the size of the message.
    /// </remarks>
    [Fact]
    public void OctetsIn_OnePictureDrawnTwice_CountsItTwice()
    {
        // Arrange
        var document = DocumentOf(PictureOf(300), PictureOf(300));

        // Act
        var octets = MailDocumentImages.OctetsIn(document);

        // Assert
        Assert.Equal(600, octets);
    }

    /// <summary>A picture nested inside a quote, a list, or a table is counted exactly as one at the top is.</summary>
    [Fact]
    public void OctetsIn_PicturesNestedInsideOtherBlocks_AreAllCounted()
    {
        // Arrange
        var document = DocumentOf(
            new MailQuoteBlock(1, [PictureOf(300)]),
            new MailListBlock(ordered: false, [new MailListItem([PictureOf(300)])]),
            new MailTableBlock(
                [],
                [new MailTableRow(IsHeader: false, [new MailTableCell(1, 1, MailBlockAlignment.Inherited, null, [PictureOf(300)])])]));

        // Act
        var octets = MailDocumentImages.OctetsIn(document);

        // Assert
        Assert.Equal(900, octets);
    }

    private static MailImageBlock PictureOf(int octets) => new(
        new MailInlineImage(
            $"data:image/png;base64,{Convert.ToBase64String(new byte[octets])}",
            AlternativeText: null,
            Width: null,
            Height: null),
        link: null,
        MailBlockAlignment.Inherited);

    private static MailDocument DocumentOf(params MailDocumentBlock[] blocks) => MailDocument.Reduced(
        blocks,
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: blocks.Length,
        undrawnInlineImageCount: 0,
        truncated: false);
}

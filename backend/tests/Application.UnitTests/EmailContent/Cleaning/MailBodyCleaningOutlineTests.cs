// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Cleaning;

/// <summary>
/// Covers what a cleaning is decided from. The outline is the whole of what may leave the deployment for this pass, so
/// these establish both halves of that: one line per top-level block with the envelope above them, and nothing per
/// nested block even where a block holds a document of its own.
/// </summary>
public sealed class MailBodyCleaningOutlineTests
{
    [Fact]
    public void Describe_AReducedDocument_NumbersOneEntryPerTopLevelBlockInReadingOrder()
    {
        // Arrange
        var document = DocumentOf(
            ParagraphSaying("Preheader nobody was meant to read"),
            new MailHeadingBlock(2, [Run("Your receipt")], MailBlockAlignment.Inherited),
            new MailSeparatorBlock());

        // Act
        var outline = MailBodyCleaningOutline.Describe(document, "Receipt 4471", "Shop");

        // Assert
        Assert.Equal("Receipt 4471", outline.Subject);
        Assert.Equal("Shop", outline.SenderName);
        Assert.Equal([0, 1, 2], outline.Blocks.Select(block => block.Index));
        Assert.Equal(
            [MailDocumentBlockType.ParagraphIdentity, MailDocumentBlockType.HeadingIdentity, MailDocumentBlockType.SeparatorIdentity],
            outline.Blocks.Select(block => block.Kind));
        Assert.Equal("Preheader nobody was meant to read", outline.Blocks[0].Opening);
        Assert.Empty(outline.Blocks[2].Opening);
    }

    /// <summary>
    /// A row of social icons is one block holding several links, and the count is what makes it recognizable as one. A
    /// nested block contributes to the line enclosing it rather than earning a line of its own, which is what keeps an
    /// answer's reach to the blocks a cleaning may actually drop.
    /// </summary>
    [Fact]
    public void Describe_ATableOfLinkedCells_IsOneEntryCountingEveryLinkBeneathIt()
    {
        // Arrange
        var document = DocumentOf(new MailTableBlock(
            [new MailTableColumn(WidthShare: null), new MailTableColumn(WidthShare: null)],
            [
                new MailTableRow(
                    IsHeader: false,
                    [
                        CellOf(LinkedParagraphSaying("Follow us")),
                        CellOf(LinkedParagraphSaying("Unsubscribe")),
                    ]),
            ]));

        // Act
        var outline = MailBodyCleaningOutline.Describe(document, subject: null, senderName: null);

        // Assert
        var block = Assert.Single(outline.Blocks);

        Assert.Equal(MailDocumentBlockType.TableIdentity, block.Kind);
        Assert.Equal(2, block.LinkCount);
        Assert.Equal("Follow us Unsubscribe", block.Opening);
    }

    /// <summary>The opening is what recognizes a block rather than a second copy of it, so a long block contributes a bounded line.</summary>
    [Fact]
    public void Describe_ABlockSayingMoreThanTheBound_ContributesOnlyAsMuchOfItAsTheBoundAllows()
    {
        // Arrange
        var document = DocumentOf(ParagraphSaying(new string('a', MailBodyCleaningOutline.MaximumOpeningLength * 3)));

        // Act
        var outline = MailBodyCleaningOutline.Describe(document, subject: null, senderName: null);

        // Assert
        Assert.Equal(MailBodyCleaningOutline.MaximumOpeningLength, Assert.Single(outline.Blocks).Opening.Length);
    }

    /// <summary>One line per block is what makes the outline readable as a list, so a sender's own line breaks are collapsed into it.</summary>
    [Fact]
    public void Describe_ABlockWhoseTextRunsOverSeveralLines_FoldsItIntoOne()
    {
        // Arrange
        var document = DocumentOf(new MailPreformattedBlock("first\r\n\tsecond   third"));

        // Act
        var outline = MailBodyCleaningOutline.Describe(document, subject: null, senderName: null);

        // Assert
        Assert.Equal("first second third", Assert.Single(outline.Blocks).Opening);
    }

    /// <summary>A picture says what it says through its alternative text, which is what tells a logo from a photograph of the thing ordered.</summary>
    [Fact]
    public void Describe_ALinkedPicture_ContributesItsAlternativeTextAndItsLink()
    {
        // Arrange
        var document = DocumentOf(new MailImageBlock(
            new MailInlineImage("cid:banner", "Spring sale", Width: 600, Height: 200),
            Link(),
            MailBlockAlignment.Inherited));

        // Act
        var outline = MailBodyCleaningOutline.Describe(document, subject: null, senderName: null);

        // Assert
        var block = Assert.Single(outline.Blocks);

        Assert.Equal("Spring sale", block.Opening);
        Assert.Equal(1, block.LinkCount);
    }

    private static MailDocument DocumentOf(params MailDocumentBlock[] blocks) => MailDocument.Reduced(
        blocks,
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 0,
        undrawnInlineImageCount: 0,
        truncated: false);

    private static MailParagraphBlock ParagraphSaying(string text) =>
        new([Run(text)], MailBlockAlignment.Inherited);

    private static MailParagraphBlock LinkedParagraphSaying(string text) =>
        new([Run(text) with { Link = Link() }], MailBlockAlignment.Inherited);

    private static MailInlineRun Run(string text) =>
        new(text, MailTextEmphasis.None, Foreground: null, Link: null);

    private static MailDocumentLink Link() =>
        new("https://example.test/go", "example.test", AsciiHost: null, MailLinkDeception.None);

    private static MailTableCell CellOf(MailDocumentBlock block) =>
        new(ColumnSpan: 1, RowSpan: 1, MailBlockAlignment.Inherited, Background: null, [block]);
}

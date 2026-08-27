// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Rendering.Document;

/// <summary>Covers reading every word out of a document and putting a guarded reading of them back.</summary>
/// <remarks>
/// This is what puts the document under the same egress guard the two textual renderings already pass through, so what
/// it has to be is exact: the walk that collects and the walk that rewrites visit the same places in the same order, and
/// a mismatch between them would put one message's words into another's position rather than fail.
/// </remarks>
public sealed class MailDocumentTextsTests
{
    /// <summary>Every word a reader would see is offered to the guard, wherever in the tree it sits.</summary>
    [Fact]
    public void Collect_ADocumentNestingEveryKindOfBlock_ReadsEveryWordItHolds()
    {
        // Arrange
        var document = Nested();

        // Act
        var texts = MailDocumentTexts.Collect(document);

        // Assert
        Assert.Equal(
            ["A heading", "In a list", "In a quote", "In a cell", "  preformatted  ", "The picture"],
            texts);
    }

    /// <summary>What the guard hands back lands where it came from, which is what makes the rewrite safe.</summary>
    [Fact]
    public void Rewrite_TheTextsTheGuardReturned_PutsEachOneBackWhereItWasRead()
    {
        // Arrange
        var document = Nested();
        var guarded = MailDocumentTexts.Collect(document)
            .Select(text => text.Replace("In", "REDACTED", StringComparison.Ordinal))
            .ToList();

        // Act
        var rewritten = MailDocumentTexts.Rewrite(document, guarded);

        // Assert
        Assert.Equal(guarded, MailDocumentTexts.Collect(rewritten));
    }

    /// <summary>Handing back what was read changes nothing, which is the case a guard that found nothing produces.</summary>
    /// <remarks>
    /// Asserted as the words and the shape rather than as the document, because a record holding lists compares those
    /// lists by reference: a rebuilt tree saying exactly the same thing is a different object and always would be.
    /// </remarks>
    [Fact]
    public void Rewrite_TheTextsUnchanged_LeavesEveryWordAndEveryBlockWhereItWas()
    {
        // Arrange
        var document = Nested();

        // Act
        var rewritten = MailDocumentTexts.Rewrite(document, MailDocumentTexts.Collect(document));

        // Assert
        Assert.Equal(MailDocumentTexts.Collect(document), MailDocumentTexts.Collect(rewritten));
        Assert.Equal(
            document.Blocks.Select(block => block.GetType().Name),
            rewritten.Blocks.Select(block => block.GetType().Name));
        Assert.Equal(document.Refusal, rewritten.Refusal);
        Assert.Equal(document.InlineImageCount, rewritten.InlineImageCount);
    }

    /// <summary>A count that does not match is a walk that has drifted, and it fails rather than misplacing words.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(12)]
    public void Rewrite_AReplacementCountThatDoesNotMatch_IsRefused(int count)
    {
        // Arrange
        var document = Nested();
        var wrong = Enumerable.Repeat("x", count).ToList();

        // Act, Assert
        Assert.ThrowsAny<ArgumentException>(() => MailDocumentTexts.Rewrite(document, wrong));
    }

    /// <summary>A link target is routing rather than prose, so it is never offered as text to be rewritten.</summary>
    /// <remarks>
    /// Deliberate: the target is what a reader is shown before a link is followed and what the deception verdict was
    /// made against, so a guard rewriting it would leave a pane showing an address the message does not go to.
    /// </remarks>
    [Fact]
    public void Collect_ADocumentWhoseWordsAreLinks_OffersTheWordsAndNotTheTargets()
    {
        // Arrange
        var link = new MailDocumentLink(
            "https://example.test/report",
            "example.test",
            AsciiHost: null,
            MailLinkDeception.None);

        var document = MailDocument.Reduced(
            [new MailParagraphBlock([Run("The report", link)], MailBlockAlignment.Inherited)],
            removedRemoteReferenceCount: 0,
            retainedRemoteImageCount: 0,
            inlineImageCount: 0,
            undrawnInlineImageCount: 0,
            truncated: false);

        // Act
        var texts = MailDocumentTexts.Collect(document);

        // Assert
        Assert.Equal(["The report"], texts);
    }

    /// <summary>A refused document holds no words, so nothing is offered and nothing is put back.</summary>
    [Fact]
    public void Collect_ARefusedDocument_ReadsNothing()
    {
        // Arrange
        var document = MailDocument.Refused(MailDocumentRefusal.NoHtmlPart);

        // Act
        var texts = MailDocumentTexts.Collect(document);

        // Assert
        Assert.Empty(texts);
    }

    /// <summary>A document holding one of every block, each with words a guard would have to see.</summary>
    private static MailDocument Nested() => MailDocument.Reduced(
        [
            new MailHeadingBlock(2, [Run("A heading")], MailBlockAlignment.Inherited),
            new MailListBlock(
                ordered: false,
                [new MailListItem([new MailParagraphBlock([Run("In a list")], MailBlockAlignment.Inherited)])]),
            new MailQuoteBlock(
                1,
                [new MailParagraphBlock([Run("In a quote")], MailBlockAlignment.Inherited)]),
            new MailTableBlock(
                [new MailTableColumn(WidthShare: null)],
                [
                    new MailTableRow(
                        IsHeader: false,
                        [
                            new MailTableCell(
                                1,
                                1,
                                MailBlockAlignment.Inherited,
                                Background: null,
                                [new MailParagraphBlock([Run("In a cell")], MailBlockAlignment.Inherited)]),
                        ]),
                ]),
            new MailPreformattedBlock("  preformatted  "),
            new MailImageBlock(
                new MailInlineImage("data:image/png;base64,AAAA", "The picture", Width: null, Height: null),
                link: null,
                MailBlockAlignment.Inherited),
            new MailSeparatorBlock(),
        ],
        removedRemoteReferenceCount: 0,
        retainedRemoteImageCount: 0,
        inlineImageCount: 1,
        undrawnInlineImageCount: 0,
        truncated: false);

    private static MailInlineRun Run(string text, MailDocumentLink? link = null) =>
        new(text, MailTextEmphasis.None, Foreground: null, link);
}

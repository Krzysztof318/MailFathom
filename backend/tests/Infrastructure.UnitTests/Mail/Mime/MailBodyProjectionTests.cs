// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>
/// Covers what a message body reduces to, and — mostly — what it never reduces to.
/// </summary>
/// <remarks>
/// <para>
/// The corpus below is hostile on purpose. A message body is markup a stranger wrote, so what this suite asserts is not
/// that a well-formed message renders nicely but that the closed document carries nothing a renderer could execute,
/// resolve, or be steered by: no script in any position, no handler, no embedded object, no form, no reference to
/// somebody else's server, and no link target outside the three schemes the reduction admits.
/// </para>
/// <para>
/// It is a reduction rather than a sanitizer, which is what makes the assertions total rather than a denylist: the
/// document has nowhere to put a handler or an element, so a construct this suite does not name cannot survive by being
/// unfamiliar. What each test therefore checks is that the readable part of a hostile message is still there.
/// </para>
/// </remarks>
public sealed class MailBodyProjectionTests
{
    /// <summary>A script is not sanitized out of the body; there is nowhere in the document for one to be.</summary>
    [Theory]
    [InlineData("<script>alert(1)</script><p>Readable</p>")]
    [InlineData("<p>Readable</p><script>alert(1)</script>")]
    [InlineData("<div><script>alert(1)</script></div><p>Readable</p>")]
    [InlineData("<p>Readable</p><noscript><script>alert(1)</script></noscript>")]
    [InlineData("<template><script>alert(1)</script></template><p>Readable</p>")]
    [InlineData("<svg><script>alert(1)</script></svg><p>Readable</p>")]
    [InlineData("<math><mtext><script>alert(1)</script></mtext></math><p>Readable</p>")]
    public async Task ProduceAsync_ScriptInAnyPosition_ReducesToTheReadablePartAlone(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.Equal(MailDocumentRefusal.None, document.Refusal);
        Assert.DoesNotContain("alert", TextOf(document), StringComparison.Ordinal);
        Assert.Contains("Readable", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>A handler is an attribute the document has no member for, so the element survives without it.</summary>
    [Theory]
    [InlineData("<p onclick=\"steal()\">Readable</p>")]
    [InlineData("<p onmouseover=\"steal()\">Readable</p>")]
    [InlineData("<img src=\"cid:none\" onerror=\"steal()\"><p>Readable</p>")]
    [InlineData("<body onload=\"steal()\"><p>Readable</p></body>")]
    public async Task ProduceAsync_EventHandlerOnAnyElement_KeepsTheContentAndCarriesNoHandler(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.Contains("Readable", TextOf(document), StringComparison.Ordinal);
        Assert.DoesNotContain("steal", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>An embedded object, a frame, and a form are dropped whole rather than reduced to something drawable.</summary>
    [Theory]
    [InlineData("<iframe src=\"https://evil.test/\"></iframe><p>Readable</p>")]
    [InlineData("<object data=\"https://evil.test/x.swf\"></object><p>Readable</p>")]
    [InlineData("<embed src=\"https://evil.test/x\"><p>Readable</p>")]
    [InlineData("<form action=\"https://evil.test/\"><input name=\"password\"><button>Send</button></form><p>Readable</p>")]
    [InlineData("<base href=\"https://evil.test/\"><p>Readable</p>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.test/\"><p>Readable</p>")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://evil.test/x.css\"><p>Readable</p>")]
    public async Task ProduceAsync_EmbeddedObjectFrameOrForm_LeavesOnlyWhatTheMessageSaid(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.Equal([nameof(MailParagraphBlock)], document.Blocks.Select(block => block.GetType().Name));
        Assert.Equal("Readable", TextOf(document));
    }

    /// <summary>A style sheet cannot reach past the message, because no style sheet reaches the document at all.</summary>
    [Fact]
    public async Task ProduceAsync_StyleSheetThatWouldReachPastTheMessage_IsNotCarried()
    {
        // Arrange
        const string Markup = """
            <style>@import url(https://evil.test/x.css); body { display: none } * { color: red }</style>
            <p>Readable</p>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        Assert.Equal([nameof(MailParagraphBlock)], document.Blocks.Select(block => block.GetType().Name));
        Assert.Equal("Readable", TextOf(document));
    }

    /// <summary>A link target outside the admitted schemes is dropped, and the words it was written on are kept.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("about:blank")]
    public async Task ProduceAsync_LinkTargetOutsideTheAdmittedSchemes_KeepsTheWordsAndDropsTheTarget(string target)
    {
        // Act
        var document = await DocumentOf($"<p><a href=\"{target}\">Press here</a></p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        Assert.Equal("Press here", run.Text);
        Assert.Null(run.Link);
    }

    /// <summary>An ordinary link keeps its target, which is what the pane shows before the link is followed.</summary>
    [Fact]
    public async Task ProduceAsync_HttpsLink_CarriesItsTargetAndItsHost()
    {
        // Act
        var document = await DocumentOf("<p><a href=\"https://example.test/report\">The report</a></p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        var link = run.Link;
        Assert.NotNull(link);
        Assert.Equal("https://example.test/report", link.Target);
        Assert.Equal("example.test", link.Host);
        Assert.Null(link.AsciiHost);
    }

    /// <summary>Words naming one host on a link that goes to another are the finding, and the deployment makes it.</summary>
    [Fact]
    public async Task ProduceAsync_LinkTextNamingADifferentHost_IsReportedAsDeceptive()
    {
        // Act
        var document = await DocumentOf(
            "<p><a href=\"https://evil.test/collect\">https://bank.test/account</a></p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        var link = run.Link;
        Assert.NotNull(link);
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, link.Deception);
        Assert.Equal("evil.test", link.Host);
    }

    /// <summary>Words naming the host the link goes to are not a finding, so an honest link is not warned about.</summary>
    [Fact]
    public async Task ProduceAsync_LinkTextNamingTheHostItGoesTo_IsNotReportedAsDeceptive()
    {
        // Act
        var document = await DocumentOf("<p><a href=\"https://bank.test/account\">www.bank.test</a></p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        Assert.NotNull(run.Link);
        Assert.Equal(MailLinkDeception.None, run.Link.Deception);
    }

    /// <summary>A host written in another script carries both spellings, which is what a homograph looks like.</summary>
    [Fact]
    public async Task ProduceAsync_HostWrittenInAnotherScript_CarriesBothSpellings()
    {
        // Act
        var document = await DocumentOf("<p><a href=\"https://раураl.test/\">Pay</a></p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        var link = run.Link;
        Assert.NotNull(link);
        Assert.NotNull(link.AsciiHost);
        Assert.NotEqual(link.Host, link.AsciiHost);
        Assert.StartsWith("xn--", link.AsciiHost, StringComparison.Ordinal);
    }

    /// <summary>A picture on somebody else's server is removed and counted rather than carried.</summary>
    [Fact]
    public async Task ProduceAsync_RemoteImageWithoutTheReadersConsent_IsRemovedAndCounted()
    {
        // Act
        var document = await DocumentOf("<p>Readable</p><img src=\"https://tracker.test/open.gif\" width=\"1\">");

        // Assert
        Assert.DoesNotContain("tracker.test", Sources(document), StringComparison.Ordinal);
        Assert.True(document.RemovedRemoteReferenceCount > 0);
        Assert.Equal(0, document.RetainedRemoteImageCount);
    }

    /// <summary>The reader's own act is what carries a remote picture, and the count says it happened.</summary>
    [Fact]
    public async Task ProduceAsync_RemoteImageTheReaderAskedFor_IsCarriedAndCounted()
    {
        // Act
        var document = await DocumentOf(
            "<p>Readable</p><img src=\"https://pictures.test/banner.png\">",
            retainRemoteImages: true);

        // Assert
        Assert.Contains("https://pictures.test/banner.png", Sources(document), StringComparison.Ordinal);
        Assert.Equal(1, document.RetainedRemoteImageCount);
    }

    /// <summary>A reference smuggled through a style declaration is counted like any other.</summary>
    [Theory]
    [InlineData("<p style=\"background: url(https://tracker.test/p.gif)\">Readable</p>")]
    [InlineData("<table><tr><td background=\"https://tracker.test/p.gif\">Readable</td></tr></table>")]
    public async Task ProduceAsync_RemoteReferenceSmuggledThroughAStyle_IsCountedAndNotCarried(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.DoesNotContain("tracker.test", Sources(document), StringComparison.Ordinal);
        Assert.True(document.RemovedRemoteReferenceCount > 0);
    }

    /// <summary>A picture the message carries itself is drawn without anything leaving the deployment.</summary>
    [Fact]
    public async Task ProduceAsync_PictureCarriedByTheMessage_BecomesSomethingNothingHasToFetch()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>Readable</p><img src=\"cid:logo@example.test\" alt=\"The logo\">",
            "--rel",
            "Content-Type: image/png",
            "Content-Id: <logo@example.test>",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("not-really-a-png")),
            "--rel--");

        // Act
        var document = await DocumentOf(content);

        // Assert
        Assert.Equal(1, document.InlineImageCount);
        Assert.Contains("data:image/png;base64,", Sources(document), StringComparison.Ordinal);
        Assert.Equal(0, document.RemovedRemoteReferenceCount);
    }

    /// <summary>A message with no markup at all is refused with the reason a reader can act on.</summary>
    [Fact]
    public async Task ProduceAsync_MessageWithNoHtmlPart_IsRefusedAsHavingNone()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Just words.");

        // Act
        var document = await DocumentOf(content);

        // Assert
        Assert.Equal(MailDocumentRefusal.NoHtmlPart, document.Refusal);
        Assert.Empty(document.Blocks);
    }

    /// <summary>Markup holding nothing drawable is refused rather than drawn as an empty pane.</summary>
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<div></div><span> </span>")]
    [InlineData("")]
    public async Task ProduceAsync_MarkupReducingToNothing_IsRefusedAsHavingNothingToDraw(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.Equal(MailDocumentRefusal.NothingRenderable, document.Refusal);
        Assert.Empty(document.Blocks);
    }

    /// <summary>The structure a reader navigates by survives the reduction, because that is what it is for.</summary>
    [Fact]
    public async Task ProduceAsync_OrdinaryStructure_KeepsHeadingsListsQuotesAndPreformattedText()
    {
        // Arrange
        const string Markup = """
            <h2>The heading</h2>
            <ul><li>First</li><li>Second</li></ul>
            <blockquote><p>Quoted</p></blockquote>
            <pre>  spaced
            lines</pre>
            <hr>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        Assert.Equal(
            [
                nameof(MailHeadingBlock),
                nameof(MailListBlock),
                nameof(MailQuoteBlock),
                nameof(MailPreformattedBlock),
                nameof(MailSeparatorBlock),
            ],
            document.Blocks.Select(block => block.GetType().Name));
        Assert.Equal(2, Assert.IsType<MailHeadingBlock>(document.Blocks[0]).Level);
        Assert.Equal(2, Assert.IsType<MailListBlock>(document.Blocks[1]).Items.Count);
        Assert.Equal(1, Assert.IsType<MailQuoteBlock>(document.Blocks[2]).Depth);
        Assert.Contains("  spaced", Assert.IsType<MailPreformattedBlock>(document.Blocks[3]).Text, StringComparison.Ordinal);
    }

    /// <summary>A table is a table, because in mail it is as often the layout as it is the data.</summary>
    [Fact]
    public async Task ProduceAsync_Table_KeepsItsColumnsRowsAndHeaderRow()
    {
        // Arrange
        const string Markup = """
            <table>
              <tr><th>Item</th><th>Amount</th></tr>
              <tr><td colspan="2">Everything</td></tr>
            </table>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        var table = Assert.IsType<MailTableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal(2, table.Rows.Count);
        Assert.True(table.Rows[0].IsHeader);
        Assert.Equal(2, table.Rows[1].Cells[0].ColumnSpan);
    }

    /// <summary>A colour the message asked for is a colour and nothing else, whichever notation it was written in.</summary>
    [Theory]
    [InlineData("color: #ff0000")]
    [InlineData("color: rgb(255, 0, 0)")]
    [InlineData("color: red")]
    public async Task ProduceAsync_ColourInAnyNotation_ReducesToTheOneNotationTheContractCarries(string declaration)
    {
        // Act
        var document = await DocumentOf($"<p style=\"{declaration}\">Readable</p>");

        // Assert
        var run = Assert.Single(Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks)).Content);
        Assert.NotNull(run.Foreground);
        Assert.Equal("#ff0000", run.Foreground.Value.Notation);
    }

    /// <summary>A body past the bound stops at it and says so, rather than being drawn as though it were whole.</summary>
    [Fact]
    public async Task ProduceAsync_BodyPastTheCharacterBound_IsTruncatedAndSaysSo()
    {
        // Arrange
        var markup = string.Concat(Enumerable.Repeat("<p>A paragraph of the message.</p>", 200));

        // Act
        var document = await DocumentOf(markup, maxBodyCharacters: 400);

        // Assert
        Assert.Equal(MailDocumentRefusal.None, document.Refusal);
        Assert.True(document.Blocks.Count < 200);
    }

    private static async Task<MailDocument> DocumentOf(
        string markup,
        bool retainRemoteImages = false,
        int maxBodyCharacters = 100_000) =>
        await DocumentOf(HtmlOnlyMessage(markup), retainRemoteImages, maxBodyCharacters);

    private static async Task<MailDocument> DocumentOf(
        StoredEmailContent content,
        bool retainRemoteImages = false,
        int maxBodyCharacters = 100_000)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(false, maxBodyCharacters, int.MaxValue)
            {
                IncludeMailDocument = true,
                RetainRemoteImageReferences = retainRemoteImages,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);

        var document = result.Rendering!.Document;
        Assert.NotNull(document);

        return document;
    }

    /// <summary>Every word the document holds, joined, which is what a leak assertion reads.</summary>
    private static string TextOf(MailDocument document) =>
        string.Join(' ', MailDocumentTexts.Collect(document)).Trim();

    /// <summary>Every address the document would have something fetch or decode.</summary>
    private static string Sources(MailDocument document) =>
        string.Join(' ', ImagesIn(document.Blocks).Select(image => image.Source));

    private static IEnumerable<MailInlineImage> ImagesIn(IReadOnlyList<MailDocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MailImageBlock picture:
                    yield return picture.Image;

                    break;

                case MailQuoteBlock quote:
                    foreach (var nested in ImagesIn(quote.Blocks))
                    {
                        yield return nested;
                    }

                    break;

                case MailListBlock list:
                    foreach (var nested in list.Items.SelectMany(item => ImagesIn(item.Blocks)))
                    {
                        yield return nested;
                    }

                    break;

                case MailTableBlock table:
                    foreach (var nested in table.Rows
                        .SelectMany(row => row.Cells)
                        .SelectMany(cell => ImagesIn(cell.Blocks)))
                    {
                        yield return nested;
                    }

                    break;

                default:
                    break;
            }
        }
    }

    private static StoredEmailContent HtmlOnlyMessage(string markup) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/html; charset=utf-8",
        string.Empty,
        markup);
}

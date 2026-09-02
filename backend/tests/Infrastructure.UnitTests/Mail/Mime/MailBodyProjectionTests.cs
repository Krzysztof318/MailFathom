// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
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
        Assert.True(document.Truncated);
    }

    /// <summary>What a link claims is judged on the words a reader is shown, not on every character the anchor holds.</summary>
    /// <remarks>
    /// Both markups put a space into the anchor's <c>TextContent</c> without putting one in front of anybody. Read that
    /// way the text stops naming a host, the claim reads as no claim, and the link to somewhere else is reported as
    /// making none — which is the quiet failure the reduction has to be immune to.
    /// </remarks>
    [Theory]
    [InlineData("<span style=\"display:none\">not </span>bank.test")]
    [InlineData("<script>var spacer = 1;</script>bank.test")]
    public async Task ProduceAsync_AnchorHidingWordsBehindItsText_IsStillJudgedOnWhatIsDrawn(string inside)
    {
        // Act
        var document = await DocumentOf($"<a href=\"https://evil.test/login\">{inside}</a>");

        // Assert
        var link = Assert.Single(LinksIn(document));
        Assert.Equal(MailLinkDeception.DisplayedHostDiffers, link.Deception);
    }

    /// <summary>An anchor whose words name where it goes claims nothing worth warning about.</summary>
    [Fact]
    public async Task ProduceAsync_AnchorNamingTheHostItGoesTo_ReportsNoDeception()
    {
        // Act
        var document = await DocumentOf("<a href=\"https://bank.test/login\"><b>bank.test</b></a>");

        // Assert
        Assert.Equal(MailLinkDeception.None, Assert.Single(LinksIn(document)).Deception);
    }

    /// <summary>A list item is read as the walk reads any element, so one asking not to be drawn is not drawn.</summary>
    [Fact]
    public async Task ProduceAsync_ListItemAskingNotToBeDrawn_IsLeftOutOfTheList()
    {
        // Act
        var document = await DocumentOf(
            "<ul><li>Shown</li><li style=\"display:none\">Hidden</li></ul>");

        // Assert
        var list = Assert.IsType<MailListBlock>(Assert.Single(document.Blocks));
        Assert.Equal("Shown", TextOf(document));
        Assert.Single(list.Items);
    }

    /// <summary>A list item's own reference to somebody else's server is counted like any other element's.</summary>
    [Fact]
    public async Task ProduceAsync_ListItemCarryingARemoteReference_HasItCounted()
    {
        // Act
        var document = await DocumentOf(
            "<ul><li style=\"background: url(https://tracker.test/p.gif)\">Shown</li></ul>");

        // Assert
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
        Assert.DoesNotContain("tracker.test", Sources(document), StringComparison.Ordinal);
    }

    /// <summary>A row asking not to be drawn is not drawn, which a table's own walk has to decide for itself.</summary>
    [Fact]
    public async Task ProduceAsync_TableRowAskingNotToBeDrawn_IsLeftOutOfTheTable()
    {
        // Arrange
        const string Markup = """
            <table>
              <tr><td>Shown</td></tr>
              <tr style="display:none"><td>Hidden</td></tr>
            </table>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        var table = Assert.IsType<MailTableBlock>(Assert.Single(document.Blocks));
        Assert.Single(table.Rows);
        Assert.DoesNotContain("Hidden", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>A table declaring more columns than the bound admits is clamped rather than believed.</summary>
    /// <remarks>
    /// The count is read from the widest row's spans, and spans multiply: a row of the permitted number of cells each
    /// claiming the permitted span declares that number squared out of a kilobyte of markup, and every column declared
    /// is an object on the answer and a definition on the thread that draws.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_TableClaimingMoreColumnsThanTheBound_IsClampedAndSaysSo()
    {
        // Arrange
        var span = MailDocumentBounds.Default.MaximumTableCells;
        var cells = string.Concat(Enumerable.Repeat($"<td colspan=\"{span}\">Wide</td>", 3));
        var markup = $"<table><tr>{cells}</tr></table>";

        // Act
        var document = await DocumentOf(markup);

        // Assert
        var table = Assert.IsType<MailTableBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MailDocumentBounds.Default.MaximumTableCells, table.Columns.Count);
        Assert.True(document.Truncated);
    }

    /// <summary>A block written as more runs than the bound admits stops at it and says the document was cut.</summary>
    /// <remarks>
    /// Alternating emphasis is what defeats the join, so this is the shape a message uses to make one paragraph cost a
    /// pane a text element per word.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_ParagraphWrittenAsMoreRunsThanTheBound_StopsAtItAndSaysSo()
    {
        // Arrange
        var runs = MailDocumentBounds.Default.MaximumRunsPerBlock + 200;
        var markup = $"<p>{string.Concat(Enumerable.Repeat("<b>a</b>b", runs))}</p>";

        // Act
        var document = await DocumentOf(markup);

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MailDocumentBounds.Default.MaximumRunsPerBlock, paragraph.Content.Count);
        Assert.True(document.Truncated);
    }

    /// <summary>A hidden element is hidden however the sender qualified the declaration that hides it.</summary>
    /// <remarks>
    /// <c>!important</c> is what a mail template writes on preheader text and on anything else meant never to be seen,
    /// so a reader comparing the whole value would draw what every other client hides — and would do it at every
    /// <c>Hidden</c> check at once rather than at one.
    /// </remarks>
    [Theory]
    [InlineData("display:none !important")]
    [InlineData("display: none !important")]
    [InlineData("visibility: hidden !important")]
    [InlineData("display:none!important")]
    public async Task ProduceAsync_HiddenDeclarationQualifiedAsImportant_IsStillHidden(string declaration)
    {
        // Act
        var document = await DocumentOf($"<p>Shown</p><div style=\"{declaration}\">Hidden</div>");

        // Assert
        Assert.Equal("Shown", TextOf(document));
    }

    /// <summary>A table section is read as the walk reads an element, so one asking not to be drawn is not drawn.</summary>
    [Fact]
    public async Task ProduceAsync_TableSectionAskingNotToBeDrawn_ContributesNoRow()
    {
        // Arrange
        const string Markup = """
            <table>
              <tbody><tr><td>Shown</td></tr></tbody>
              <tbody style="display:none"><tr><td>Hidden</td></tr></tbody>
            </table>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        var table = Assert.IsType<MailTableBlock>(Assert.Single(document.Blocks));
        Assert.Single(table.Rows);
        Assert.DoesNotContain("Hidden", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>A table section's own reference to somebody else's server is counted like any other element's.</summary>
    [Fact]
    public async Task ProduceAsync_TableSectionCarryingARemoteReference_HasItCountedOnce()
    {
        // Arrange
        const string Markup = """
            <table>
              <tbody background="https://tracker.test/p.gif"><tr><td>Shown</td></tr></tbody>
            </table>
            """;

        // Act
        var document = await DocumentOf(Markup);

        // Assert
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
        Assert.DoesNotContain("tracker.test", Sources(document), StringComparison.Ordinal);
    }

    /// <summary>What the body element itself asked to load is counted, since the walk never meets that element.</summary>
    [Theory]
    [InlineData("<body background=\"https://tracker.test/px.gif\"><p>Readable</p></body>")]
    [InlineData("<body style=\"background-image:url(https://tracker.test/px.gif)\"><p>Readable</p></body>")]
    public async Task ProduceAsync_RemoteReferenceOnTheBodyElement_IsCounted(string markup)
    {
        // Act
        var document = await DocumentOf(markup);

        // Assert
        Assert.Equal("Readable", TextOf(document));
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
    }

    /// <summary>A picture the message carries and names by its location is drawn from the message rather than fetched.</summary>
    [Fact]
    public async Task ProduceAsync_PictureNamedByItsContentLocation_IsDrawnFromTheMessage()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>Readable</p><img src=\"https://sender.test/logo.png\">",
            "--rel",
            "Content-Type: image/png",
            "Content-Location: https://sender.test/logo.png",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("not-really-a-png")),
            "--rel--");

        // Act
        var document = await DocumentOf(content);

        // Assert
        Assert.Equal(1, document.InlineImageCount);
        Assert.Contains("data:image/png;base64,", Sources(document), StringComparison.Ordinal);
        Assert.DoesNotContain("sender.test", Sources(document), StringComparison.Ordinal);
        Assert.Equal(0, document.RemovedRemoteReferenceCount);
    }

    /// <summary>A width no arithmetic can use leaves the column without one rather than producing a value nothing can write.</summary>
    /// <remarks>
    /// <c>double.TryParse</c> answers an overflowing literal with infinity rather than failing, and a share resolved
    /// against an infinite total is <c>NaN</c> — which the serializer refuses, so the reader would lose the whole
    /// message over one attribute.
    /// </remarks>
    [Theory]
    [InlineData("1e400px")]
    [InlineData("1e400")]
    [InlineData("1e400%")]
    public async Task ProduceAsync_ColumnWidthNoArithmeticCanUse_LeavesTheColumnWithoutOne(string width)
    {
        // Act
        var document = await DocumentOf($"<table><tr><td width=\"{width}\">Readable</td></tr></table>");

        // Assert
        var table = Assert.IsType<MailTableBlock>(Assert.Single(document.Blocks));
        var share = Assert.Single(table.Columns).WidthShare;
        Assert.True(share is null || double.IsFinite(share.Value));
    }

    /// <summary>A picture emitted more often than the bound allows stops at it, and the document says so.</summary>
    /// <remarks>
    /// The octets are spent on what the answer carries rather than on what was decoded: one part resolves once and
    /// every reference naming it emits the whole encoding again, so a body repeating a single <c>cid:</c> reference
    /// composes an answer many times the size of the message. Past what a reading pane accepts, that loses the reader
    /// the words as well as the pictures.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_OnePictureDrawnMoreOftenThanTheBoundAllows_StopsAtItAndSaysSo()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            string.Concat(Enumerable.Repeat("<p><img src=\"cid:one@example.test\"></p>", 20)),
            "--rel",
            "Content-Type: image/png",
            "Content-Id: <one@example.test>",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(new byte[600]),
            "--rel--");

        // Act
        var document = await DocumentOf(content, maxImageOctets: 3000);

        // Assert
        Assert.Equal(5, ImagesIn(document.Blocks).Count());
        Assert.Equal(15, document.UndrawnInlineImageCount);
        Assert.True(document.Truncated);
    }

    /// <summary>A reference somebody's server would answer is counted even where the element asked not to be drawn.</summary>
    /// <remarks>
    /// A tracking pixel is a hidden picture, so a reduction that dropped a hidden element without reading it told the
    /// reader the message asked to load nothing in exactly the case it was asking to load something.
    /// </remarks>
    [Theory]
    [InlineData("<img src=\"https://tracker.test/p.gif\" style=\"display:none\">")]
    [InlineData("<div style=\"display:none;background-image:url(https://tracker.test/p.gif)\">x</div>")]
    [InlineData("<div style=\"display:none\"><img src=\"https://tracker.test/p.gif\"></div>")]
    [InlineData("<ul><li style=\"display:none\"><img src=\"https://tracker.test/p.gif\"></li></ul>")]
    [InlineData("<table><tr style=\"display:none\"><td><img src=\"https://tracker.test/p.gif\"></td></tr></table>")]
    [InlineData("<table><tr><td style=\"display:none\"><img src=\"https://tracker.test/p.gif\"></td></tr></table>")]
    [InlineData("<table><tbody style=\"display:none\"><tr><td><img src=\"https://tracker.test/p.gif\"></td></tr></tbody></table>")]
    public async Task ProduceAsync_HiddenReferenceToSomebodysServer_IsStillCounted(string markup)
    {
        // Act
        var document = await DocumentOf($"<p>Readable</p>{markup}");

        // Assert
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
        Assert.DoesNotContain("tracker.test", Sources(document), StringComparison.Ordinal);
    }

    /// <summary>A picture the message itself carries is not counted as a removed reference because it was hidden.</summary>
    [Fact]
    public async Task ProduceAsync_HiddenPictureTheMessageCarries_IsNotCountedAsRemoved()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>Readable</p><img src=\"cid:one@example.test\" style=\"display:none\">",
            "--rel",
            "Content-Type: image/png",
            "Content-Id: <one@example.test>",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(new byte[16]),
            "--rel--");

        // Act
        var document = await DocumentOf(content);

        // Assert
        Assert.Equal(0, document.RemovedRemoteReferenceCount);
    }

    /// <summary>A style attribute past the bound still hides what it asked to hide, wherever it wrote the declaration.</summary>
    /// <remarks>
    /// Reading nothing past the bound made length the way to defeat every hiding check at once, and reading only the
    /// prefix moves that rather than closing it: the padding has to precede the declaration instead of following it.
    /// So the hiding declaration is read out of the whole attribute and this covers both sides of the padding.
    /// </remarks>
    [Theory]
    [InlineData("display:none;{0}")]
    [InlineData("{0}display:none")]
    [InlineData("{0}visibility:hidden")]
    [InlineData("{0}display:none !important")]
    public async Task ProduceAsync_HidingDeclarationBuriedUnderAnOverLongAttribute_IsStillHidden(string shape)
    {
        // Arrange
        var padding = string.Concat(Enumerable.Repeat("color:#112233;", 500));
        var declarations = string.Format(CultureInfo.InvariantCulture, shape, padding);

        // Act
        var document = await DocumentOf($"<p>Readable</p><div style=\"{declarations}\">Hidden</div>");

        // Assert
        Assert.DoesNotContain("Hidden", TextOf(document), StringComparison.Ordinal);
        Assert.Contains("Readable", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>An attribute past the bound still applies the properties that fit, so the cut loses only the rest.</summary>
    [Fact]
    public async Task ProduceAsync_AnOverLongAttributeStatingNoHiding_KeepsWhatFitsWithinTheBound()
    {
        // Arrange
        var padding = string.Concat(Enumerable.Repeat("color:#112233;", 500));

        // Act
        var document = await DocumentOf($"<div style=\"text-align:center;{padding}\">Readable</div>");

        // Assert
        var paragraph = Assert.IsType<MailParagraphBlock>(Assert.Single(document.Blocks));
        Assert.Equal(MailBlockAlignment.Center, paragraph.Alignment);
    }

    /// <summary>A picture a heading holds survives beside it rather than being dropped with the words it is not.</summary>
    /// <remarks>
    /// A masthead is written as a logo inside an <c>h1</c>, and a heading is read as words — so the block the picture
    /// became had nowhere to go, and both it and the alt text left the document with nothing saying they had.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_HeadingHoldingAPicture_KeepsItBesideTheHeading()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<h1>Acme <img src=\"cid:one@example.test\" alt=\"Acme\"></h1>",
            "--rel",
            "Content-Type: image/png",
            "Content-Id: <one@example.test>",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(new byte[16]),
            "--rel--");

        // Act
        var document = await DocumentOf(content);

        // Assert
        Assert.Contains(document.Blocks, block => block is MailHeadingBlock);
        var picture = Assert.Single(ImagesIn(document.Blocks));
        Assert.Equal("Acme", picture.AlternativeText);
        Assert.StartsWith("data:image/png;base64,", picture.Source, StringComparison.Ordinal);
    }

    /// <summary>A run cut at a bound is cut between characters rather than through one.</summary>
    /// <remarks>
    /// The single letter in front of the emoji is what puts the bound in the middle of an astral pair rather than
    /// between two of them, which is the offset a sender chooses. The lone surrogate that a raw slice would leave is
    /// replaced by a UTF-8 writer and rejected by a JSON one, so the reader loses the whole message rather than the
    /// tail of one paragraph — and the round trip below is the assertion because it is the same encode the response
    /// performs on the way out of this boundary.
    /// </remarks>
    [Theory]
    [InlineData("<p>x{0}</p>")]
    [InlineData("<pre>x{0}</pre>")]
    public async Task ProduceAsync_TextCutWhereAnAstralPairFalls_LeavesNoHalfCharacter(string shape)
    {
        // Arrange
        var written = string.Format(
            CultureInfo.InvariantCulture,
            shape,
            string.Concat(Enumerable.Repeat("\U0001F600", 30_000)));

        // Act
        var document = await DocumentOf(written, maxBodyCharacters: 200_000);

        // Assert
        var text = TextOf(document);
        Assert.True(document.Truncated);
        Assert.NotEmpty(text);
        Assert.Equal(text, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>A preformatted block cut at the bound says so, because what it holds is meant to be complete.</summary>
    /// <remarks>
    /// A log or a diff arriving with its tail dropped and nothing saying so reads as the whole of what the sender
    /// wrote, which is the one thing a block that preserves its own formatting must not do.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_PreformattedBlockPastTheCharacterBound_SaysItWasCut()
    {
        // Arrange
        var written = new string('a', 30_000);

        // Act
        var document = await DocumentOf($"<pre>{written}</pre>", maxBodyCharacters: 200_000);

        // Assert
        var preformatted = Assert.IsType<MailPreformattedBlock>(Assert.Single(document.Blocks));
        Assert.Equal(20_000, preformatted.Text.Length);
        Assert.True(document.Truncated);
    }

    /// <summary>A picture the message wrote into its own markup that no allow-list admits is reported as undrawn.</summary>
    /// <remarks>
    /// The same loss reached through a part of the message is counted where the part is decoded, and reached through
    /// the document's budget where a picture is charged against it. This is the third way to it, and a reader is owed
    /// the same sentence about all three. An SVG is the media type the allow-list exists to exclude: it is a document
    /// that can carry script and name a remote address, which is everything the reduction takes out of a message.
    /// </remarks>
    [Theory]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("data:text/html;base64,PGgxLz4=")]
    public async Task ProduceAsync_ADataUriNoAllowListAdmits_DrawsNothingAndSaysSo(string source)
    {
        // Act
        var document = await DocumentOf($"<p>Readable</p><img src=\"{source}\" alt=\"A picture\">");

        // Assert
        Assert.Empty(ImagesIn(document.Blocks));
        Assert.Equal(1, document.UndrawnInlineImageCount);
        Assert.True(document.Truncated);
    }

    /// <summary>A picture written into the markup past what one picture may weigh is reported as undrawn too.</summary>
    [Fact]
    public async Task ProduceAsync_ADataUriPastWhatOnePictureMayWeigh_DrawsNothingAndSaysSo()
    {
        // Arrange
        var written = Convert.ToBase64String(new byte[(2 * 1024 * 1024) + 1]);

        // Act
        var document = await DocumentOf(
            $"<p>Readable</p><img src=\"data:image/png;base64,{written}\">",
            maxBodyCharacters: 4_000_000);

        // Assert
        Assert.Empty(ImagesIn(document.Blocks));
        Assert.Equal(1, document.UndrawnInlineImageCount);
        Assert.True(document.Truncated);
    }

    /// <summary>A picture the markup writes within every bound is drawn from the markup itself.</summary>
    /// <remarks>The affirmative beside the two refusals above, so the allow-list is shown to admit as well as refuse.</remarks>
    [Fact]
    public async Task ProduceAsync_ADataUriTheAllowListAdmits_IsDrawnFromTheMarkup()
    {
        // Arrange
        var written = Convert.ToBase64String(new byte[64]);

        // Act
        var document = await DocumentOf($"<img src=\"data:image/png;base64,{written}\" alt=\"A picture\">");

        // Assert
        var picture = Assert.Single(ImagesIn(document.Blocks));
        Assert.StartsWith("data:image/png;base64,", picture.Source, StringComparison.Ordinal);
        Assert.Equal(0, document.UndrawnInlineImageCount);
    }

    /// <summary>The words a list holds outside its items are kept, because that is what they are anywhere else.</summary>
    /// <remarks>
    /// A parser foster-parents stray content out of a table and leaves it in place in a list, so a sentence written
    /// beside two items is ordinary content of the message rather than markup nobody meant. It is emitted before the
    /// list, which is where the message put it.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_WordsAListHoldsOutsideItsItems_AreKept()
    {
        // Act
        var document = await DocumentOf("<ul><div>Sale ends today</div><li>One</li><li>Two</li></ul>");

        // Assert
        Assert.Contains("Sale ends today", TextOf(document), StringComparison.Ordinal);
        var list = Assert.IsType<MailListBlock>(document.Blocks[^1]);
        Assert.Equal(2, list.Items.Count);
    }

    /// <summary>A reference a list carries outside its items is counted, as one anywhere else on the walk is.</summary>
    [Fact]
    public async Task ProduceAsync_AReferenceAListCarriesOutsideItsItems_IsCounted()
    {
        // Act
        var document = await DocumentOf(
            "<ul><div style=\"background-image:url(https://tracker.test/p.gif)\">Tracked</div><li>One</li></ul>");

        // Assert
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
        Assert.Contains("Tracked", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>Every link the document carries, in reading order.</summary>
    private static IEnumerable<MailDocumentLink> LinksIn(MailDocument document) => document.Blocks
        .OfType<MailParagraphBlock>()
        .SelectMany(paragraph => paragraph.Content)
        .Select(run => run.Link)
        .OfType<MailDocumentLink>()
        .Distinct();

    private static async Task<MailDocument> DocumentOf(
        string markup,
        bool retainRemoteImages = false,
        int maxBodyCharacters = 100_000,
        int maxImageOctets = int.MaxValue) =>
        await DocumentOf(HtmlOnlyMessage(markup), retainRemoteImages, maxBodyCharacters, maxImageOctets);

    private static async Task<MailDocument> DocumentOf(
        StoredEmailContent content,
        bool retainRemoteImages = false,
        int maxBodyCharacters = 100_000,
        int maxImageOctets = int.MaxValue)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(false, maxBodyCharacters, int.MaxValue)
            {
                IncludeMailDocument = true,
                RetainRemoteImageReferences = retainRemoteImages,
                RemainingInlineImageOctetsForRead = maxImageOctets,
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

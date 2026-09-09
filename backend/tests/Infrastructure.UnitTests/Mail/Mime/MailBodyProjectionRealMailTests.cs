// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// Covers what mail as it actually circulates reduces to, which is a different question from what a hostile message
/// reduces to.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MailBodyProjectionTests" /> beside this asks whether anything a stranger wrote can survive the reduction
/// as a capability. This file asks the opposite question about the same walk: whether what a stranger wrote is still
/// <em>readable</em> afterwards. The corpus is therefore ordinary rather than adversarial — a forward out of Outlook, a
/// reply chain out of Gmail, a marketing newsletter of nested layout tables, markup with tags nobody closed, and a
/// message with no markup at all — because those are what the pane is drawing most of the time and each of them used
/// to draw badly in a way no assertion here would have caught.
/// </para>
/// <para>
/// Every assertion is over the document rather than over pixels. What a reader is owed is stated as a property of the
/// tree — a table that is layout is not a table block, the words of the message come before the words it quotes, an
/// address is a link, an alignment that described a removed box is not inherited by the text — and the client's own
/// suites are where those become a screen.
/// </para>
/// </remarks>
public sealed class MailBodyProjectionRealMailTests
{
    /// <summary>What a marketing message is: a wrapper table centring a content table, with a call to action in a cell.</summary>
    private const string Newsletter = """
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f4f4f4">
          <tr><td align="center" style="text-align:center">
            <table width="600" cellpadding="0" cellspacing="0" border="0">
              <tr><td style="font-family:Arial">
                <h1>Your April statement</h1>
                <p>Hello Marta, your statement for April is ready. The balance carried forward is 640.00 PLN.</p>
                <p>Questions? Write to us at billing@example.test or read www.example.test/help.</p>
              </td></tr>
              <tr><td align="center" bgcolor="#3b82f6" style="text-align:center">
                <a href="https://example.test/statement">Open the statement</a>
              </td></tr>
              <tr><td>
                <img src="https://tracker.example.test/pixel.gif" width="1" height="1" alt="">
                <p>You receive this because you hold an account with us.</p>
              </td></tr>
            </table>
          </td></tr>
        </table>
        """;

    /// <summary>What Outlook writes when somebody forwards a message, conditional comment and namespaced tag included.</summary>
    private const string OutlookForward = """
        <html xmlns:o="urn:schemas-microsoft-com:office:office">
        <head><!--[if gte mso 9]><xml><o:OfficeDocumentSettings/></xml><![endif]-->
        <style>p.MsoNormal { margin: 0 }</style></head>
        <body lang="EN-GB">
        <div class="WordSection1">
        <p class="MsoNormal">Passing this on for your records.<o:p></o:p></p>
        <p class="MsoNormal"><o:p>&nbsp;</o:p></p>
        <div><div style="border:none;border-top:solid #E1E1E1 1.0pt;padding:3.0pt 0cm 0cm 0cm">
        <p class="MsoNormal"><b>From:</b> Piotr Zielinski &lt;piotr@example.test&gt;<br>
        <b>Sent:</b> 12 March 2027 09:14<br>
        <b>To:</b> Marta Nowak &lt;marta@example.test&gt;<br>
        <b>Subject:</b> Re: Delivery window</p>
        </div></div>
        <p class="MsoNormal">The courier will arrive between 10.00 and 13.00.<o:p></o:p></p>
        </div>
        </body></html>
        """;

    /// <summary>What a reply out of Gmail is: an attribution line and a quotation, four deep.</summary>
    private const string GmailReplyChain = """
        <div dir="ltr">That works for me, thanks.</div>
        <div class="gmail_quote">
          <div dir="ltr" class="gmail_attr">On Tue, 9 Mar 2027 at 08:02, Ewa Sikora &lt;ewa@example.test&gt; wrote:<br></div>
          <blockquote class="gmail_quote" style="margin:0 0 0 .8ex;border-left:1px #ccc solid;padding-left:1ex">
            <div dir="ltr">Shall we say Thursday?</div>
            <blockquote class="gmail_quote" style="margin:0 0 0 .8ex;border-left:1px #ccc solid;padding-left:1ex">
              <div dir="ltr">I am free later in the week.</div>
              <blockquote class="gmail_quote" style="margin:0 0 0 .8ex;border-left:1px #ccc solid;padding-left:1ex">
                <div dir="ltr">When suits you?</div>
              </blockquote>
            </blockquote>
          </blockquote>
        </div>
        """;

    /// <summary>What Apple Mail writes when somebody forwards, which is a cited quotation and an interchange break.</summary>
    private const string AppleMailForward = """
        <html><body style="word-wrap:break-word">
        <div>Forwarding as promised.</div><div><br class="Apple-interchange-newline"></div>
        <blockquote type="cite">
          <div>Begin forwarded message:</div>
          <div><b>From:</b> Robert Lis &lt;robert@example.test&gt;</div>
          <div>The room is booked for Friday.</div>
        </blockquote>
        </body></html>
        """;

    /// <summary>Markup nobody closed, a cell outside any table, emphasis across a block, and a document inside a document.</summary>
    private const string BrokenMarkup = """
        <div><p>First paragraph that nobody closed
        <p>Second paragraph, also unclosed
        <td>A cell that belongs to no table</td>
        <b>Emphasis that opens here <p>and crosses into the next block</b>
        </div></div></span>
        <html><body><p>A second document inside the first</p></body></html>
        """;

    /// <summary>A message that displays no markup at all, which is most of what a mailbox receives.</summary>
    private const string PlainTextOnly = """
        Your order 4412 has shipped.

        Track it at https://example.test/track/4412 or open www.example.test/orders.
        Reply to support@example.test if anything is wrong.

        --
        The example.test team
        """;

    [Fact]
    public async Task ProduceAsync_Newsletter_DrawsNoTableBecauseEveryTableInItIsLayout()
    {
        // Act
        var document = await DocumentOf(Newsletter);

        // Assert
        Assert.Equal(MailDocumentRefusal.None, document.Refusal);
        Assert.Empty(BlocksIn(document.Blocks).OfType<MailTableBlock>());
        Assert.Contains("Your April statement", TextOf(document), StringComparison.Ordinal);
        Assert.Contains("You receive this because you hold an account with us.", TextOf(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_NewsletterCentredByItsWrapper_LeavesTheTextItselfUnaligned()
    {
        // Act
        var document = await DocumentOf(Newsletter);

        // Assert
        var statement = BlocksIn(document.Blocks)
            .OfType<MailParagraphBlock>()
            .First(paragraph => TextIn(paragraph).StartsWith("Hello Marta", StringComparison.Ordinal));

        Assert.Equal(MailBlockAlignment.Inherited, statement.Alignment);
    }

    [Fact]
    public async Task ProduceAsync_NewsletterCallToAction_KeepsItsWordsAsAFollowableLink()
    {
        // Act
        var document = await DocumentOf(Newsletter);

        // Assert
        var link = Assert.Single(LinksIn(document), candidate => candidate.Target == "https://example.test/statement");
        Assert.Equal("example.test", link.Host);
    }

    [Fact]
    public async Task ProduceAsync_NewsletterTrackingPixel_IsCountedRatherThanCarried()
    {
        // Act
        var document = await DocumentOf(Newsletter);

        // Assert
        Assert.Equal(1, document.RemovedRemoteReferenceCount);
        Assert.DoesNotContain("tracker.example.test", TextOf(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_OutlookForward_KeepsTheSendersOwnWordsAboveWhatTheyForwarded()
    {
        // Act
        var document = await DocumentOf(OutlookForward);

        // Assert
        var text = TextOf(document);

        Assert.Contains("Passing this on for your records.", text, StringComparison.Ordinal);
        Assert.Contains("The courier will arrive between 10.00 and 13.00.", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("Passing this on", StringComparison.Ordinal)
            < text.IndexOf("The courier", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProduceAsync_OutlookForward_CarriesNoStylesheetAndNoConditionalMarkupAsWords()
    {
        // Act
        var document = await DocumentOf(OutlookForward);

        // Assert
        var text = TextOf(document);

        Assert.DoesNotContain("MsoNormal", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OfficeDocumentSettings", text, StringComparison.Ordinal);
        Assert.DoesNotContain("margin: 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_OutlookForward_KeepsTheHeaderLinesOfTheMessageBeingForwarded()
    {
        // Act
        var document = await DocumentOf(OutlookForward);

        // Assert
        var text = TextOf(document);

        Assert.Contains("piotr@example.test", text, StringComparison.Ordinal);
        Assert.Contains("Re: Delivery window", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_GmailReplyChain_CarriesTheQuotationAsNestedQuoteBlocks()
    {
        // Act
        var document = await DocumentOf(GmailReplyChain);

        // Assert
        Assert.Equal(
            [1, 2, 3],
            [.. BlocksIn(document.Blocks).OfType<MailQuoteBlock>().Select(quote => quote.Depth)]);
        Assert.Contains("That works for me, thanks.", TextOf(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProduceAsync_GmailReplyChain_EndsOnTheQuotationSoAReaderCanFoldItAway()
    {
        // Act
        var document = await DocumentOf(GmailReplyChain);

        // Assert
        Assert.IsType<MailQuoteBlock>(document.Blocks[^1]);
        Assert.IsType<MailParagraphBlock>(document.Blocks[0]);
    }

    [Fact]
    public async Task ProduceAsync_AppleMailForward_KeepsTheContributionOutsideTheCitedBlock()
    {
        // Act
        var document = await DocumentOf(AppleMailForward);

        // Assert
        Assert.Contains("Forwarding as promised.", TextIn((MailParagraphBlock)document.Blocks[0]), StringComparison.Ordinal);
        Assert.Contains(
            "The room is booked for Friday.",
            TextOf(document),
            StringComparison.Ordinal);
        Assert.Single(BlocksIn(document.Blocks).OfType<MailQuoteBlock>());
    }

    [Fact]
    public async Task ProduceAsync_MarkupNobodyClosed_KeepsEveryReadablePartInTheOrderItWasWritten()
    {
        // Act
        var document = await DocumentOf(BrokenMarkup);

        // Assert
        var text = TextOf(document);

        foreach (var written in new[]
        {
            "First paragraph that nobody closed",
            "Second paragraph, also unclosed",
            "A cell that belongs to no table",
            "Emphasis that opens here",
            "and crosses into the next block",
            "A second document inside the first",
        })
        {
            Assert.Contains(written, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ProduceAsync_MessageWithNoMarkup_ReducesItsPlainTextToADocument()
    {
        // Act
        var document = await DocumentOf(PlainTextMessage(PlainTextOnly));

        // Assert
        Assert.Equal(MailDocumentRefusal.None, document.Refusal);
        Assert.Equal(3, document.Blocks.Count);
        Assert.Contains("Your order 4412 has shipped.", TextOf(document), StringComparison.Ordinal);
    }

    /// <summary>A message written with the line endings the wire uses is not reported as having been cut short.</summary>
    /// <remarks>
    /// The message arrives with the line endings the wire uses, and the reduction normalizes them — so a bound measured
    /// against what arrived rather than against what is reduced reports every plain-text message as cut short by
    /// however many lines it had.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_MessageWithNoMarkupWrittenWithCarriageReturns_IsNotReportedAsCutShort()
    {
        // Act
        var document = await DocumentOf(PlainTextMessage(PlainTextOnly.ReplaceLineEndings("\r\n")));

        // Assert
        Assert.False(document.Truncated);
    }

    [Fact]
    public async Task ProduceAsync_MessageWithNoMarkup_CarriesTheAddressesItWroteAsWords()
    {
        // Act
        var document = await DocumentOf(PlainTextMessage(PlainTextOnly));

        // Assert
        Assert.Equal(
            ["https://example.test/track/4412", "https://www.example.test/orders", "mailto:support@example.test"],
            [.. LinksIn(document).Select(link => link.Target)]);
    }

    /// <summary>Every block the document holds, nested ones included, which is what a corpus assertion reads.</summary>
    private static IEnumerable<MailDocumentBlock> BlocksIn(IEnumerable<MailDocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;

            var nested = block switch
            {
                MailQuoteBlock quote => quote.Blocks,
                MailListBlock list => [.. list.Items.SelectMany(item => item.Blocks)],
                MailTableBlock table =>
                    (IReadOnlyList<MailDocumentBlock>)[.. table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks)],
                _ => [],
            };

            foreach (var inner in BlocksIn(nested))
            {
                yield return inner;
            }
        }
    }

    /// <summary>Every link the document carries, in reading order and once each.</summary>
    private static IEnumerable<MailDocumentLink> LinksIn(MailDocument document) => BlocksIn(document.Blocks)
        .OfType<MailParagraphBlock>()
        .SelectMany(paragraph => paragraph.Content)
        .Select(run => run.Link)
        .OfType<MailDocumentLink>()
        .Distinct();

    private static string TextIn(MailParagraphBlock paragraph) =>
        string.Concat(paragraph.Content.Select(run => run.Text));

    private static string TextOf(MailDocument document) =>
        string.Join(' ', MailDocumentTexts.Collect(document)).Trim();

    private static StoredEmailContent PlainTextMessage(string text) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/plain; charset=utf-8",
        string.Empty,
        text);

    private static StoredEmailContent HtmlOnlyMessage(string markup) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/html; charset=utf-8",
        string.Empty,
        markup);

    private static Task<MailDocument> DocumentOf(string markup) => DocumentOf(HtmlOnlyMessage(markup));

    private static async Task<MailDocument> DocumentOf(StoredEmailContent content)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(false, 100_000, int.MaxValue)
            {
                IncludeMailDocument = true,
                RemainingInlineImageOctetsForRead = int.MaxValue,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);

        var document = result.Rendering!.Document;
        Assert.NotNull(document);

        return document;
    }
}

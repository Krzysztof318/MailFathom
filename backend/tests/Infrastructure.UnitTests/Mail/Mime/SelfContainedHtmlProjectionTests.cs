// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>
/// Covers the representation a full-HTML surface reads: the sender's own layout, with its own pictures in it, and with
/// nothing left that a renderer would run or resolve against another host.
/// </summary>
/// <remarks>
/// <para>
/// The two properties are asserted rather than described, and asserted generally. <see cref="Executable" /> and
/// <see cref="RemoteAddresses" /> read the produced markup by parsing it — which is what a renderer does with it — and
/// answer what is left rather than whether a named construct was handled. So a test naming a vector is naming an
/// example of the property, and a construct nobody here thought of is covered by the same assertion.
/// </para>
/// <para>
/// Parsing the output is also the only way to see the class of defect that matters most here: markup that survives one
/// parser and means something else to the next. Every assertion below therefore runs against a second reading of the
/// string rather than against the string itself.
/// </para>
/// </remarks>
public sealed class SelfContainedHtmlProjectionTests
{
    /// <summary>The elements a renderer would run something out of, whatever else they carry.</summary>
    private static readonly string[] ExecutableElements =
    [
        "script", "iframe", "object", "embed", "applet", "frame", "frameset", "form", "input", "button", "base",
        "meta", "link", "svg", "math", "noscript", "template", "source",
    ];

    /// <summary>Nothing executable survives, whichever construct the message wrote it as.</summary>
    [Theory]
    [InlineData("<script>fetch('https://evil.test/')</script><p>Readable</p>")]
    [InlineData("<p>Readable</p><noscript><script>fetch('https://evil.test/')</script></noscript>")]
    [InlineData("<template><script>fetch('https://evil.test/')</script></template><p>Readable</p>")]
    [InlineData("<svg><script>fetch('https://evil.test/')</script></svg><p>Readable</p>")]
    [InlineData("<svg onload=\"fetch('https://evil.test/')\"></svg><p>Readable</p>")]
    [InlineData("<svg><a xlink:href=\"javascript:fetch(1)\"><text>x</text></a></svg><p>Readable</p>")]
    [InlineData("<math><mtext><script>fetch('https://evil.test/')</script></mtext></math><p>Readable</p>")]
    [InlineData("<math href=\"javascript:fetch(1)\"><mi>x</mi></math><p>Readable</p>")]
    [InlineData("<p onclick=\"fetch('https://evil.test/')\">Readable</p>")]
    [InlineData("<img src=\"cid:none\" onerror=\"fetch('https://evil.test/')\"><p>Readable</p>")]
    [InlineData("<body onload=\"fetch('https://evil.test/')\"><p>Readable</p></body>")]
    [InlineData("<base href=\"https://evil.test/\"><p>Readable</p>")]
    [InlineData("<p xml:base=\"https://evil.test/\">Readable</p>")]
    [InlineData("<form action=\"https://evil.test/\"><input name=\"password\"></form><p>Readable</p>")]
    [InlineData("<button formaction=\"https://evil.test/\">Send</button><p>Readable</p>")]
    [InlineData("<iframe srcdoc=\"&lt;script&gt;fetch(1)&lt;/script&gt;\"></iframe><p>Readable</p>")]
    [InlineData("<object data=\"https://evil.test/x.swf\"></object><p>Readable</p>")]
    [InlineData("<embed src=\"https://evil.test/x\"><p>Readable</p>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.test/\"><p>Readable</p>")]
    [InlineData("<a href=\"javascript:fetch('https://evil.test/')\">Readable</a>")]
    [InlineData("<a href=\"JaVaScRiPt:fetch(1)\">Readable</a>")]
    [InlineData("<a href=\"&#106;avascript:fetch(1)\">Readable</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">Readable</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD5mZXRjaCgxKTwvc2NyaXB0Pg==\">Readable</a>")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9J2ZldGNoKDEpJy8+\"><p>Readable</p>")]
    [InlineData("<style>@import url(https://evil.test/x.css);</style><p>Readable</p>")]
    [InlineData("<style>p{background:url('javascript:fetch(1)')}</style><p>Readable</p>")]
    [InlineData("<p style=\"background:url('javascript:fetch(1)')\">Readable</p>")]
    [InlineData("<style>x{}</style><script>fetch(1)</script><p>Readable</p>")]
    [InlineData("<listing><img src=x onerror=fetch(1)></listing><p>Readable</p>")]
    [InlineData("<noembed><img src=x onerror=fetch(1)></noembed><p>Readable</p>")]
    [InlineData("<xmp><img src=x onerror=fetch(1)></xmp><p>Readable</p>")]
    [InlineData("<p unheardof=\"javascript:fetch(1)\">Readable</p>")]
    [InlineData("<p data-go=\"javascript:fetch(1)\">Readable</p>")]
    public async Task ProduceAsync_ExecutableConstruct_LeavesNothingARendererWouldRun(string markup)
    {
        // Act
        var html = await HtmlOf(markup);

        // Assert
        Assert.Empty(Executable(html));
    }

    /// <summary>Nothing reaches another host, whichever form the address was written in.</summary>
    [Theory]
    [InlineData("<img src=\"https://evil.test/pixel.gif\"><p>Readable</p>")]
    [InlineData("<img src=\"//evil.test/pixel.gif\"><p>Readable</p>")]
    [InlineData("<img srcset=\"https://evil.test/a.png 1x, https://evil.test/b.png 2x\"><p>Readable</p>")]
    [InlineData("<picture><source srcset=\"https://evil.test/a.webp\"><img src=\"cid:none\"></picture><p>Readable</p>")]
    [InlineData("<td background=\"https://evil.test/bg.png\">Readable</td>")]
    [InlineData("<p style=\"background-image:url(https://evil.test/bg.png)\">Readable</p>")]
    [InlineData("<style>p{background-image:url(https://evil.test/bg.png)}</style><p>Readable</p>")]
    [InlineData("<style>@import url('https://evil.test/x.css');p{color:red}</style><p>Readable</p>")]
    [InlineData("<style>@font-face{font-family:x;src:url(https://evil.test/x.woff2)}</style><p>Readable</p>")]
    [InlineData("<style>@media screen{p{background:url(https://evil.test/bg.png)}}</style><p>Readable</p>")]
    [InlineData("<p data-tracker=\"https://evil.test/beacon\">Readable</p>")]
    [InlineData("<p unheardof=\"https://evil.test/beacon\">Readable</p>")]
    [InlineData("<img longdesc=\"https://evil.test/beacon\" src=\"cid:none\"><p>Readable</p>")]
    [InlineData("<video poster=\"https://evil.test/beacon.png\"></video><p>Readable</p>")]
    [InlineData("<blockquote cite=\"https://evil.test/beacon\">Readable</blockquote>")]
    public async Task ProduceAsync_RemoteAddressInAnyForm_LeavesNoneOfIt(string markup)
    {
        // Act
        var html = await HtmlOf(markup);

        // Assert
        Assert.Empty(RemoteAddresses(html));
        Assert.Contains("Readable", html, StringComparison.Ordinal);
    }

    /// <summary>A link's target is kept, because a target is a navigation the reader makes rather than a resource the document pulls.</summary>
    [Theory]
    [InlineData("<a href=\"https://example.test/offer\">Readable</a>", "https://example.test/offer")]
    [InlineData("<a href=\"mailto:sales@example.test\">Readable</a>", "mailto:sales@example.test")]
    public async Task ProduceAsync_LinkTarget_SurvivesSoTheLayoutIsStillUsable(string markup, string expected)
    {
        // Act
        var html = await HtmlOf(markup);

        // Assert
        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    /// <summary>The layout survives, which is the whole reason this representation exists beside the sanitized one.</summary>
    [Fact]
    public async Task ProduceAsync_StyleAndPresentation_AreKeptRatherThanStripped()
    {
        // Act
        var html = await HtmlOf(
            "<style>.lead{font-size:20px}</style>"
                + "<table cellpadding=\"8\" bgcolor=\"#eeeeee\"><tr><td class=\"lead\" "
                + "style=\"color:#336699;text-align:center\">Readable</td></tr></table>");

        // Assert
        Assert.Contains("font-size", html, StringComparison.Ordinal);
        Assert.Contains("text-align", html, StringComparison.Ordinal);
        Assert.Contains("class=\"lead\"", html, StringComparison.Ordinal);
        Assert.Contains("bgcolor", html, StringComparison.Ordinal);
    }

    /// <summary>A picture the message carries is inlined as what it actually is, so the layout draws with nothing fetched.</summary>
    [Fact]
    public async Task ProduceAsync_PictureCarriedByTheMessage_ArrivesAsADataUriOfItsOwnMediaType()
    {
        // Act
        var html = await HtmlOf(MessageCarryingItsOwnPicture(
            "<p>Readable</p><img src=\"cid:logo@example.test\" alt=\"The logo\">"));

        // Assert
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Empty(RemoteAddresses(html));
    }

    /// <summary>The two representations that inline a picture share one octet budget, so a read asking for both does not draw it twice.</summary>
    [Fact]
    public async Task ProduceAsync_DocumentAskedForBeside_DrawsOnWhatTheDocumentLeftOfTheOctetBudget()
    {
        // Arrange
        var content = MessageCarryingItsOwnPicture("<p>Readable</p><img src=\"cid:logo@example.test\">");

        var wholePicture = await RenderAsync(content, retainRemoteReferences: false, includeMailDocument: true);
        Assert.NotNull(wholePicture.Document);
        var pictureOctets = (int)MailDocumentImages.OctetsIn(wholePicture.Document);
        Assert.NotEqual(0, pictureOctets);

        // Act
        var rendering = await RenderAsync(
            content,
            retainRemoteReferences: false,
            maximumImageOctets: pictureOctets,
            includeMailDocument: true);

        // Assert
        Assert.NotNull(rendering.Document);
        Assert.Equal(pictureOctets, MailDocumentImages.OctetsIn(rendering.Document));

        var markup = rendering.SelfContainedHtmlBody;
        Assert.NotNull(markup);
        Assert.DoesNotContain("data:image/png;base64,", markup.Text, StringComparison.Ordinal);
        Assert.Equal(EmailBodyTruncation.InlineImageOctetLimit, markup.Truncation);
    }

    /// <summary>An anchor pointing at the message's own part is decided by the link policy rather than answered with that part's octets.</summary>
    [Fact]
    public async Task ProduceAsync_AnchorNamingTheMessagesOwnPicture_IsNotAnsweredWithThePicture()
    {
        // Act
        var html = await HtmlOf(MessageCarryingItsOwnPicture(
            "<p>Readable</p><a href=\"cid:logo@example.test\">The logo</a>"));

        // Assert
        Assert.DoesNotContain("href=\"data:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cid:", html, StringComparison.Ordinal);
        Assert.Contains("The logo", html, StringComparison.Ordinal);
    }

    /// <summary>A picture the message answers out of its own parts under an absolute address is inlined rather than removed as remote.</summary>
    [Fact]
    public async Task ProduceAsync_PictureNamedByTheLocationItWasSentWith_IsAnsweredOutOfTheMessage()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>Readable</p><img src=\"https://cdn.example.test/logo.png\">",
            "--rel",
            "Content-Type: image/png",
            "Content-Location: https://cdn.example.test/logo.png",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("not-really-a-png")),
            "--rel--");

        // Act
        var html = await HtmlOf(content);

        // Assert
        Assert.Contains("data:image/png;base64,", html, StringComparison.Ordinal);
        Assert.Empty(RemoteAddresses(html));
    }

    /// <summary>The reader's consent restores every address, because a layout missing its fonts is the reduction it was opened to escape.</summary>
    [Theory]
    [InlineData("<img src=\"https://cdn.example.test/hero.png\"><p>Readable</p>", "https://cdn.example.test/hero.png")]
    [InlineData(
        "<style>@font-face{font-family:x;src:url(https://cdn.example.test/x.woff2)}</style><p>Readable</p>",
        "https://cdn.example.test/x.woff2")]
    [InlineData(
        "<p style=\"background-image:url(https://cdn.example.test/bg.png)\">Readable</p>",
        "https://cdn.example.test/bg.png")]
    public async Task ProduceAsync_ConsentedRead_KeepsTheAddressesTheLayoutResolves(string markup, string expected)
    {
        // Act
        var html = await HtmlOf(markup, retainRemoteReferences: true);

        // Assert
        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    /// <summary>Consent restores addresses and nothing else, so what the reader agreed to is not read as agreeing to more.</summary>
    [Theory]
    [InlineData("<script>fetch('https://evil.test/')</script><p>Readable</p>")]
    [InlineData("<p onclick=\"fetch('https://evil.test/')\">Readable</p>")]
    [InlineData("<iframe src=\"https://evil.test/\"></iframe><p>Readable</p>")]
    [InlineData("<a href=\"javascript:fetch(1)\">Readable</a>")]
    public async Task ProduceAsync_ConsentedRead_StillLeavesNothingARendererWouldRun(string markup)
    {
        // Act
        var html = await HtmlOf(markup, retainRemoteReferences: true);

        // Assert
        Assert.Empty(Executable(html));
    }

    /// <summary>A stylesheet the sender imported stays refused even under consent, because what it fetches is a document nothing here read.</summary>
    [Fact]
    public async Task ProduceAsync_ConsentedRead_StillRefusesAnImportedStylesheet()
    {
        // Act
        var html = await HtmlOf(
            "<style>@import url('https://cdn.example.test/x.css');</style><p>Readable</p>",
            retainRemoteReferences: true);

        // Assert
        Assert.DoesNotContain("x.css", html, StringComparison.Ordinal);
    }

    /// <summary>Markup past the character bound is cut, and the cut is stated rather than left to be inferred.</summary>
    [Fact]
    public async Task ProduceAsync_MarkupPastTheBound_ReportsTheBoundThatCutIt()
    {
        // Arrange
        var markup = "<p>" + new string('a', 4000) + "</p>";

        // Act
        var representation = await RepresentationOf(markup, maxBodyCharacters: 500);

        // Assert
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, representation.Truncation);
        Assert.Equal(markup.Length, representation.OriginalCharacterCount);
    }

    /// <summary>Markup that serializes past the bound its source fitted is brought back inside it, because closing tags are growth too.</summary>
    /// <remarks>
    /// Deeply nested markup spends its allowance on opening tags and needs as much again to close them, so a source cut
    /// to the bound can serialize well past it. The bound is what a caller relies on, so it governs the result rather
    /// than the input it was produced from.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_DeeplyNestedMarkup_HoldsTheResultToTheCharacterBound()
    {
        // Arrange
        const int maxBodyCharacters = 2_000;
        var markup = string.Concat(Enumerable.Repeat("<div>", 600))
            + "Readable"
            + string.Concat(Enumerable.Repeat("</div>", 600));

        // Act
        var representation = await RepresentationOf(markup, maxBodyCharacters: maxBodyCharacters);

        // Assert
        Assert.True(
            representation.Text.Length <= maxBodyCharacters,
            $"the result held {representation.Text.Length} characters against a bound of {maxBodyCharacters}");
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, representation.Truncation);
    }

    /// <summary>Words shaped like an inlined picture buy the sender no room, because the bound discounts what was inlined rather than what looks inlined.</summary>
    /// <remarks>
    /// The discount exists for the pictures this pass put into the result. Reading it back out of the string instead
    /// would let a message carry its own overshoot past the bound by writing a long unbroken word beginning with
    /// <c>data:</c>, which is a bound a sender can talk their way out of.
    /// </remarks>
    [Fact]
    public async Task ProduceAsync_SenderTextShapedLikeAPicture_IsStillHeldToTheCharacterBound()
    {
        // Arrange
        // Sized so the two readings disagree: the source fits the bound, the serialization overshoots it by less than
        // the token is long, and the closing tags are written by the serializer rather than by the sender. Discounting
        // the token would therefore end the loop on the first pass with a result past the bound.
        const int maxBodyCharacters = 2_000;
        var wordsShapedLikeAPicture = "data:image/png;base64," + new string('A', 578);
        var markup = $"<p>{wordsShapedLikeAPicture}</p>"
            + string.Concat(Enumerable.Repeat("<div>", 150))
            + "Readable";

        // Act
        var representation = await RepresentationOf(markup, maxBodyCharacters: maxBodyCharacters);

        // Assert
        Assert.True(
            representation.Text.Length <= maxBodyCharacters,
            $"the result held {representation.Text.Length} characters against a bound of {maxBodyCharacters}");
    }

    /// <summary>A message's own pictures are discounted from the character bound, because they are bounded in octets instead.</summary>
    [Fact]
    public async Task ProduceAsync_PictureLongerThanTheCharacterBound_IsStillInlined()
    {
        // Act
        var representation = await RepresentationOf(
            MessageCarryingItsOwnPicture("<p>Readable</p><img src=\"cid:logo@example.test\">"),
            maxBodyCharacters: 60);

        // Assert
        Assert.Contains("data:image/png;base64,", representation.Text, StringComparison.Ordinal);
    }

    /// <summary>A picture past the octet bound is left out, and that is reported rather than drawn as an absent picture.</summary>
    [Fact]
    public async Task ProduceAsync_PicturePastTheOctetBound_ReportsThatItWasLeftOut()
    {
        // Act
        var representation = await RepresentationOf(
            MessageCarryingItsOwnPicture("<p>Readable</p><img src=\"cid:logo@example.test\">"),
            maximumImageOctets: 1);

        // Assert
        Assert.Equal(EmailBodyTruncation.InlineImageOctetLimit, representation.Truncation);
        Assert.True(representation.WasTruncated);
    }

    /// <summary>A message that displays no markup produces no representation, rather than an empty one nothing can tell apart.</summary>
    [Fact]
    public async Task ProduceAsync_MessageWithNoHtmlPart_ProducesNoRepresentation()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Just words.");

        // Act
        var rendering = await RenderAsync(content, retainRemoteReferences: false);

        // Assert
        Assert.Null(rendering.SelfContainedHtmlBody);
    }

    /// <summary>A read that did not ask for it pays for none of it, which is what keeps the tool surface unchanged.</summary>
    [Fact]
    public async Task RenderAsync_WithoutTheFlag_ProducesNoRepresentation()
    {
        // Arrange
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        // Act
        var result = await renderer.RenderAsync(
            HtmlOnlyMessage("<p>Readable</p>"),
            new EmailContentRenderingBounds(IncludeSanitizedHtml: false, 100_000, int.MaxValue),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Rendering!.SelfContainedHtmlBody);
    }

    /// <summary>Every element and attribute a renderer would act on, out of a fresh parse of what was produced.</summary>
    /// <remarks>
    /// The assertion is the absence of a category rather than of a named construct: an element outside the HTML
    /// namespace, one of the elements that runs something, an event handler however it was spelled, and a value naming
    /// a scripting scheme. A construct this suite never anticipated is reported by the same reading.
    /// </remarks>
    private static IReadOnlyList<string> Executable(string html)
    {
        var parsed = new HtmlParser().ParseDocument(html);

        return
        [
            .. parsed.All
                .Where(element => element.LocalName is not ("html" or "head" or "body"))
                .SelectMany(element => Findings(element)),
        ];
    }

    private static IEnumerable<string> Findings(IElement element)
    {
        if (element.NamespaceUri != NamespaceNames.HtmlUri
            || ExecutableElements.Contains(element.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            yield return element.LocalName;
        }

        foreach (var attribute in element.Attributes)
        {
            if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                || attribute.NamespaceUri is not null
                || Scripting(attribute.Value))
            {
                yield return $"{element.LocalName}@{attribute.Name}";
            }
        }
    }

    private static bool Scripting(string value)
    {
        var trimmed = value.AsSpan().Trim(" \t\r\n\f\0".AsSpan());

        return trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every absolute address left anywhere in the produced markup, including inside its style.</summary>
    /// <remarks>
    /// Read off the whole string rather than off the attributes, because a stylesheet's declarations are text a renderer
    /// resolves exactly as it resolves an attribute, and an assertion that only walked the DOM would miss them.
    /// </remarks>
    private static IReadOnlyList<string> RemoteAddresses(string html) =>
    [
        .. html
            .Split(['"', '\'', '(', ')', ' ', ',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token =>
                token.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || token.StartsWith("//", StringComparison.Ordinal)),
    ];

    private static async Task<string> HtmlOf(string markup, bool retainRemoteReferences = false) =>
        await HtmlOf(HtmlOnlyMessage(markup), retainRemoteReferences);

    private static async Task<string> HtmlOf(StoredEmailContent content, bool retainRemoteReferences = false) =>
        (await RepresentationOf(content, retainRemoteReferences)).Text;

    private static async Task<EmailBodyRepresentation> RepresentationOf(
        string markup,
        bool retainRemoteReferences = false,
        int maxBodyCharacters = 100_000,
        int maximumImageOctets = int.MaxValue) =>
        await RepresentationOf(
            HtmlOnlyMessage(markup),
            retainRemoteReferences,
            maxBodyCharacters,
            maximumImageOctets);

    private static async Task<EmailBodyRepresentation> RepresentationOf(
        StoredEmailContent content,
        bool retainRemoteReferences = false,
        int maxBodyCharacters = 100_000,
        int maximumImageOctets = int.MaxValue)
    {
        var rendering = await RenderAsync(
            content,
            retainRemoteReferences,
            maxBodyCharacters,
            maximumImageOctets);

        var representation = rendering.SelfContainedHtmlBody;
        Assert.NotNull(representation);

        return representation;
    }

    private static async Task<EmailContentRendering> RenderAsync(
        StoredEmailContent content,
        bool retainRemoteReferences,
        int maxBodyCharacters = 100_000,
        int maximumImageOctets = int.MaxValue,
        bool includeMailDocument = false)
    {
        var renderer = new MimeKitEmailContentRenderer(new EmailMimeExtractionOptions { MaxPartCount = 1000 });

        var result = await renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(IncludeSanitizedHtml: false, maxBodyCharacters, int.MaxValue)
            {
                IncludeMailDocument = includeMailDocument,
                IncludeSelfContainedHtml = true,
                RetainRemoteImageReferences = retainRemoteReferences,
                RemainingInlineImageOctetsForRead = maximumImageOctets,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);
        Assert.NotNull(result.Rendering);

        return result.Rendering;
    }

    private static StoredEmailContent HtmlOnlyMessage(string markup) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/html; charset=utf-8",
        string.Empty,
        markup);

    private static StoredEmailContent MessageCarryingItsOwnPicture(string markup) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: multipart/related; boundary=\"rel\"",
        string.Empty,
        "--rel",
        "Content-Type: text/html; charset=utf-8",
        string.Empty,
        markup,
        "--rel",
        "Content-Type: image/png",
        "Content-Id: <logo@example.test>",
        "Content-Transfer-Encoding: base64",
        string.Empty,
        Convert.ToBase64String(Encoding.UTF8.GetBytes("not-really-a-png")),
        "--rel--");
}

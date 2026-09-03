// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Produces the message's own markup with its pictures in it and nothing that runs or reaches another host.</summary>
/// <remarks>
/// <para>
/// The surface this feeds opens the sender's layout in a frame beside the reading pane, so what it needs is the
/// opposite of what the two representations before it produce: the sanitized markup keeps no style and admits no URI
/// scheme at all, and the document tree carries no markup by construction. This one keeps the layout and answers the
/// two questions a frame cannot — what the markup may run, and what it may fetch — because neither is a question the
/// HTML Standard's sandboxing flags answer.
/// </para>
/// <para>
/// One pass over the message's own markup, serialized once. It is not produced from the sanitized representation's
/// output: that would make one pass's serialization another pass's input, which is the arrangement mutation attacks are
/// built out of, and it would start from markup that had already lost everything this surface exists to show.
/// </para>
/// <para>
/// Both properties are allow-lists rather than deny-lists, which is what makes the assertion behind them general. An
/// element nobody anticipated is not kept, an attribute nobody anticipated is not kept, and a URL is decided by
/// <see cref="Resolved" /> rather than by the name of whatever attribute or declaration carried it — so a remote
/// address in a form this file never names is removed because nothing admitted it, not because something matched it.
/// </para>
/// <para>
/// Nothing here reaches the network. The message's own pictures are resolved out of its own parts and inlined as
/// <c>data:</c> URIs, and every other reference is either removed or, where the reader asked for this message's remote
/// content, left exactly as the sender wrote it.
/// </para>
/// </remarks>
internal static partial class SelfContainedHtmlProjection
{
    /// <summary>The elements the layout is drawn out of, which is the sanitized representation's list plus what carries style.</summary>
    /// <remarks>
    /// <c>style</c> and <c>picture</c> are the two additions. Everything that runs is absent by not being here —
    /// <c>script</c>, <c>iframe</c>, <c>object</c>, <c>embed</c>, <c>applet</c>, <c>frame</c>, <c>form</c>,
    /// <c>meta</c>, <c>link</c>, <c>base</c>, and <c>svg</c> among them — and a disallowed element is removed with its
    /// content rather than unwrapped, so a script's body does not survive as words the message never displayed.
    /// </remarks>
    private static readonly string[] AllowedElements =
    [
        "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br", "caption",
        "center", "cite", "code", "col", "colgroup", "dd", "del", "dfn", "div", "dl", "dt", "em", "figcaption",
        "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "ins", "kbd",
        "li", "main", "mark", "nav", "ol", "p", "picture", "pre", "q", "s", "samp", "section", "small", "span",
        "strike", "strong", "style", "sub", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "tt", "u",
        "ul", "var", "wbr",
    ];

    /// <summary>The attributes a layout is described with, which is presentation, structure, and the two that carry a picture.</summary>
    /// <remarks>
    /// <para>
    /// <c>style</c>, <c>class</c>, and <c>id</c> are here because this surface exists to show the sender's design and
    /// a stylesheet with no selectors to match is not one. Every event handler is absent by not being named, which is
    /// the same mechanism that removes an attribute nobody thought of.
    /// </para>
    /// <para>
    /// <c>srcset</c> and <c>sizes</c> are deliberately absent. A candidate list is several addresses in one attribute
    /// value, so it is neither resolvable to one picture nor removable one address at a time; dropping it costs the
    /// reader the alternative resolutions and keeps the <c>src</c> beside it, which is the picture the message would
    /// have drawn anyway. <c>source</c> falls with them for the same reason, while <c>picture</c> stays so the
    /// <c>img</c> inside it survives.
    /// </para>
    /// </remarks>
    private static readonly string[] AllowedAttributes =
    [
        "abbr", "align", "alt", "background", "bgcolor", "border", "cellpadding", "cellspacing", "char", "charoff",
        "class", "color", "colspan", "dir", "face", "headers", "height", "href", "hspace", "id", "lang", "name",
        "nowrap", "rowspan", "scope", "size", "span", "src", "start", "style", "summary", "title", "type", "valign",
        "vspace", "width",
    ];

    /// <summary>Produces the representation for one message, or reports that the message displays no markup.</summary>
    /// <param name="message">The parsed message, which the pictures are resolved out of.</param>
    /// <param name="htmlParts">The HTML the message displays, in the order the walk found the parts.</param>
    /// <param name="retainRemoteReferences">Whether the reader asked for this message's remote content.</param>
    /// <param name="allowance">How much markup may be reduced, and which bound to name when it cuts.</param>
    /// <param name="maximumImageOctets">How many octets of its own pictures this representation may still inline.</param>
    /// <param name="cancellationToken">Cancels the decode of the message's own pictures.</param>
    /// <returns>The representation, or <see langword="null" /> where the message carries no HTML body part or the result did not pass <see cref="NothingRunsIn" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either reference argument is <see langword="null" />.</exception>
    internal static async Task<EmailBodyRepresentation?> ProduceAsync(
        MimeMessage message,
        IReadOnlyList<string> htmlParts,
        bool retainRemoteReferences,
        EmailBodyCharacterAllowance allowance,
        int maximumImageOctets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(htmlParts);

        if (htmlParts.Count == 0)
        {
            return null;
        }

        var joined = string.Join('\n', htmlParts);
        var source = MailTextBounds.TruncateAtTextElementBoundary(joined, allowance.MaxCharacters);

        var bounds = MailDocumentBounds.Default;
        var images = await MailInlineImages.ResolveAsync(
            message,
            ReferencesIn(source),
            bounds.MaximumInlineImages,
            bounds.MaximumInlineImageOctets,
            Math.Min(bounds.MaximumInlineImageOctetsPerDocument, maximumImageOctets),
            cancellationToken);

        var html = PolicyFor(images, retainRemoteReferences).Sanitize(source);

        return NothingRunsIn(html)
            ? new EmailBodyRepresentation(html, joined.Length, TruncationOf(source, joined, images, allowance))
            : null;
    }

    /// <summary>Answers whether the serialized result carries nothing a renderer would execute.</summary>
    /// <remarks>
    /// <para>
    /// The policy above is what removes an executable construct and this is what proves it was removed, which are two
    /// different jobs: the policy decides against the source, and a renderer acts on the serialization. Between them
    /// sits the one step neither can see — the parse a renderer performs on the string it is handed — and markup that
    /// serializes to something a second parser reads differently is exactly the class of attack this representation
    /// cannot afford to be wrong about, because whatever draws it is where a message would run.
    /// </para>
    /// <para>
    /// So the result is parsed once more and held against the same two allow-lists that produced it, in the HTML
    /// namespace they were written for. Nothing about that parse reaches a reader: what is returned either way is the
    /// string the single serialization produced, and a result that fails is not repaired and not returned at all. A
    /// failure here is a defect in the pass above rather than a property of the message, and the honest answer to a
    /// defect at a trust boundary is to hand back nothing and let the reduced tree beside it be what the reader sees.
    /// </para>
    /// </remarks>
    private static bool NothingRunsIn(string html)
    {
        var parsed = new HtmlParser().ParseDocument(html);

        return parsed.All.Where(element => !StructureOf(element)).All(element =>
            element.NamespaceUri == NamespaceNames.HtmlUri
            && AllowedElements.Contains(element.LocalName, StringComparer.OrdinalIgnoreCase)
            && element.Attributes.All(attribute =>
                attribute.NamespaceUri is null
                && AllowedAttributes.Contains(attribute.LocalName, StringComparer.OrdinalIgnoreCase)
                && !CarriesScript(attribute.Value)));
    }

    /// <summary>Answers whether an element is one the parser supplied rather than one the message wrote.</summary>
    /// <remarks>
    /// The pass produces a fragment, so parsing it back puts a document around it. Those three are the wrapper rather
    /// than content, and neither allow-list was ever written to hold them.
    /// </remarks>
    private static bool StructureOf(IElement element) =>
        element.NamespaceUri == NamespaceNames.HtmlUri
        && element.LocalName is "html" or "head" or "body";

    /// <summary>Answers whether a value names something a renderer would execute rather than draw.</summary>
    /// <remarks>
    /// Read after the parse rather than off the source, so an encoded form, a leading control character, and the
    /// whitespace a browser strips have all already been resolved into the value a renderer would act on.
    /// </remarks>
    private static bool CarriesScript(string value)
    {
        var trimmed = value.AsSpan().Trim(" \t\r\n\f\u0000".AsSpan());

        return trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:image/svg+xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Names which bound removed something, or that none did.</summary>
    /// <remarks>
    /// The character bound is reported ahead of the picture bound where both bit, because it is the one that removed
    /// words: a reader told the message was cut short reads that as being handed part of a message, which is true of
    /// both, while a reader told only about pictures would take the words in front of them for the whole of it.
    /// </remarks>
    private static EmailBodyTruncation TruncationOf(
        string source,
        string joined,
        MailInlineImages images,
        EmailBodyCharacterAllowance allowance) => source.Length < joined.Length
            ? allowance.TruncationWhenCut
            : images.UndrawnCount > 0
                ? EmailBodyTruncation.InlineImageOctetLimit
                : EmailBodyTruncation.None;

    /// <summary>Names every reference the markup might resolve against the message's own parts.</summary>
    /// <remarks>
    /// <para>
    /// Read out of the markup as text rather than out of a parse of it, because the parse that decides everything else
    /// is the sanitizing one and the pictures have to be resolved before it runs. Scanning is enough for what this
    /// answers: it decides which of the message's own parts are worth decoding, and naming one too many costs a decode
    /// while naming one too few costs the reader a picture the message carried.
    /// </para>
    /// <para>
    /// Absolute addresses are named beside content identifiers because mail routinely references its own parts by the
    /// <c>Content-Location</c> they were sent with. Such an address is answered out of the message rather than fetched,
    /// which is why it survives the removal below: it never was a reference to somebody else's host.
    /// </para>
    /// </remarks>
    private static HashSet<string> ReferencesIn(string source)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var match in EmbeddedReference().EnumerateMatches(source))
        {
            var reference = source.Substring(match.Index, match.Length);

            named.Add(MailInlineImages.KeyOf(reference));

            if (Uri.TryCreate(reference, UriKind.Absolute, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                named.Add(MailInlineImages.KeyOf(absolute.AbsoluteUri));
            }
        }

        return named;
    }

    /// <summary>Builds the policy one message is served under, which is the whole of what this representation promises.</summary>
    /// <remarks>
    /// <para>
    /// A policy per message rather than one held between them, because it closes over what this message resolved and
    /// over one reader's consent about it. Sharing an instance would mean mutating a handler that another read is
    /// already inside.
    /// </para>
    /// <para>
    /// <c>@import</c> is refused whatever the reader asked for, and it is the one address the consent does not restore.
    /// What it fetches is a stylesheet, and a stylesheet fetched at render time is a document nothing here parsed, so
    /// admitting it would hand the frame CSS that never met an allow-list.
    /// </para>
    /// <para>
    /// <c>@font-face</c> is refused on the same terms as any other remote reference, but by removing the rule rather
    /// than the address inside it: the sanitizer resolves a declaration's URL through <see cref="Resolved" /> in a
    /// style attribute and inside <c>@media</c>, and does not reach the <c>src</c> of a font face — so the only place
    /// the removal can be made is the rule. A consented read admits it, which is what puts the sender's own typeface
    /// back on the same footing as the sender's own pictures.
    /// </para>
    /// </remarks>
    private static HtmlSanitizer PolicyFor(MailInlineImages images, bool retainRemoteReferences)
    {
        var policy = new HtmlSanitizer();

        Replace(policy.AllowedTags, AllowedElements);
        Replace(policy.AllowedAttributes, AllowedAttributes);
        Replace(
            policy.AllowedSchemes,
            retainRemoteReferences
                ? ["cid", "data", "mailto", "tel", "http", "https"]
                : ["cid", "data", "mailto", "tel"]);

        // Narrowed rather than replaced, because the set the sanitizer ships with holds the ordinary style rule every
        // declaration lives in: a list written from scratch here would keep the at-rules and drop the stylesheet.
        policy.AllowedAtRules.Remove(CssRuleType.Import);

        if (retainRemoteReferences)
        {
            policy.AllowedAtRules.Add(CssRuleType.FontFace);
        }
        else
        {
            policy.AllowedAtRules.Remove(CssRuleType.FontFace);
        }

        policy.AllowDataAttributes = false;

        policy.FilterUrl += (_, filtering) =>
            filtering.SanitizedUrl = Resolved(filtering, images, retainRemoteReferences);

        return policy;
    }

    /// <summary>Decides one URL, wherever in the markup or the style it was written.</summary>
    /// <remarks>
    /// <para>
    /// The one place a reference is judged, so a form of reference this file never anticipated is judged by the same
    /// four rules as the ones it does: a content identifier becomes the picture itself, an address the message answers
    /// out of its own parts becomes that picture too, a link's target is left alone because nothing fetches it, and
    /// everything else that names another host survives only where the reader asked for this message's remote content.
    /// </para>
    /// <para>
    /// A <c>data:</c> URI the sender wrote is narrowed to the pictures a message may carry, which is what keeps
    /// <c>data:text/html</c> out of a frame that would treat it as a document.
    /// </para>
    /// </remarks>
    private static string? Resolved(FilterUrlEventArgs filtering, MailInlineImages images, bool retainRemoteReferences)
    {
        var reference = filtering.OriginalUrl.Trim();
        var absolute = Uri.TryCreate(reference, UriKind.Absolute, out var parsed) ? parsed : null;

        // A scheme-relative reference names another host exactly as an absolute one does: whatever renders the markup
        // supplies the scheme out of its own address, so this is a remote address that merely declines to say which
        // protocol it wants. It parses as relative, which is what would otherwise carry it past the branch below.
        if (reference.StartsWith("//", StringComparison.Ordinal))
        {
            return retainRemoteReferences ? reference : null;
        }

        // A link's target is a navigation the reader performs rather than a resource the document pulls, so removing it
        // would make the surface less useful than the reduced tree beside it without making it safer. It is decided
        // ahead of the substitutions below rather than after them, because those answer a reference with the picture it
        // names: an anchor pointing at the message's own logo is a link, and answering it with that logo's octets would
        // give a reader a target shape this policy never names and nothing here bounds.
        if (filtering.Tag.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            return absolute is null
                ? filtering.SanitizedUrl
                : absolute.Scheme is "http" or "https" or "mailto" or "tel" ? reference : null;
        }

        if (reference.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            return images.Resolve(reference);
        }

        if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return MailDataUri.Drawable(reference, MailDocumentBounds.Default.MaximumInlineImageOctets);
        }

        if (absolute is null)
        {
            // A relative reference resolves against a document that has no address, so it reaches nothing. It is left
            // as the sender wrote it rather than removed, because removing it would take a picture's alternative text
            // and a link's shape with it while gaining nothing.
            return filtering.SanitizedUrl;
        }

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
        {
            return filtering.SanitizedUrl;
        }

        return images.Resolve(reference)
            ?? images.Resolve(absolute.AbsoluteUri)
            ?? (retainRemoteReferences ? reference : null);
    }

    private static void Replace(ISet<string> allowList, IEnumerable<string> allowed)
    {
        allowList.Clear();
        allowList.UnionWith(allowed);
    }

    /// <summary>Matches a reference the message might answer out of its own parts, bounded so crafted markup costs one request.</summary>
    [GeneratedRegex(
        """(?:cid:|https?://)[^\s"'()<>\\]+""",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmbeddedReference();
}

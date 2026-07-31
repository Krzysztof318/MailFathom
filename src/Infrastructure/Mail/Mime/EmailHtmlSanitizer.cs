// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Ganss.Xss;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reduces an HTML mail body to markup that is safe to hand to whatever renders it.</summary>
/// <remarks>
/// <para>
/// Message HTML is hostile input. It is written by anyone who can send mail, it is read by clients and by models, and
/// the ways it can misbehave — script, event handlers, embedded objects, forms, and above all references that a
/// renderer resolves on its own — are not a list anyone can finish. The policy is therefore an allow-list at every
/// level the library offers: elements, attributes, CSS properties, CSS at-rules, and URI schemes. A deny-list cannot be
/// proven complete, and this is the one place in MailFathom where being wrong is an injected script rather than a bad
/// answer.
/// </para>
/// <para>
/// No URI scheme is allowed at all, which is stricter than removing what an attribute might auto-fetch. Nothing here
/// can prove which attributes a given client resolves without being asked, so no reference survives to find out: no
/// remote image is fetched, no linked resource is loaded, and no tracking URL is left for a renderer to open. A
/// <c>cid:</c> reference falls with them, and deliberately — it points at a part of the same message, this read never
/// returns part bytes, and a client that resolved content identifiers against something other than the message would
/// follow it somewhere unintended. The inline-resource count is what a caller reports instead.
/// </para>
/// <para>
/// A disallowed element is removed with its content rather than unwrapped. Unwrapping would keep the text a
/// <c>&lt;script&gt;</c> element carries, which is inert but indistinguishable from the message's own words. The
/// element allow-list is therefore generous about the presentational elements mail actually uses, so removing an
/// element is rare and never silently eats a paragraph.
/// </para>
/// <para>
/// <c>template</c> is not on the allow-list and must never be added: its contents were the subject of GHSA-j92c-7v7g-gj3f,
/// which is fixed in the pinned version and was only ever exploitable where the element had been allowed explicitly.
/// </para>
/// </remarks>
internal sealed class EmailHtmlSanitizer
{
    /// <summary>The elements a mail body may keep, which is structure, emphasis, lists, and tables.</summary>
    /// <remarks>
    /// The presentational elements mail is full of — <c>font</c>, <c>center</c>, <c>big</c> — are allowed rather than
    /// removed even though nothing renders them meaningfully any more, because their attributes are stripped anyway and
    /// removing the element would take the words inside it with them.
    /// </remarks>
    private static readonly string[] AllowedElements =
    [
        "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "big", "blockquote", "br", "caption",
        "center", "cite", "code", "col", "colgroup", "dd", "del", "dfn", "div", "dl", "dt", "em", "figcaption",
        "figure", "font", "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "ins", "kbd",
        "li", "main", "mark", "nav", "ol", "p", "pre", "q", "s", "samp", "section", "small", "span", "strike",
        "strong", "sub", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "tt", "u", "ul", "var", "wbr",
    ];

    /// <summary>The attributes a mail body may keep.</summary>
    /// <remarks>
    /// Every one of them describes the content rather than pointing at something or styling it. <c>alt</c> and
    /// <c>title</c> are what a stripped image still says, which is worth more to a reader than the image was; the table
    /// spans keep a table's shape readable; <c>dir</c> and <c>lang</c> are what a right-to-left message needs to render
    /// as it was written. Absent by design are <c>style</c>, <c>class</c>, <c>id</c>, every event handler, and every
    /// attribute that carries a URI.
    /// </remarks>
    private static readonly string[] AllowedAttributes =
    [
        "alt", "colspan", "dir", "lang", "rowspan", "title",
    ];

    private readonly HtmlSanitizer sanitizer = CreatePolicy();

    /// <summary>Sanitizes one HTML body.</summary>
    /// <param name="html">The body markup as the message wrote it.</param>
    /// <returns>Markup carrying no script, no event handler, and no reference a renderer could resolve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="html" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// No base URI is supplied, so a relative reference is never resolved into an absolute one. Nothing depends on that
    /// today, because no URI attribute survives the policy at all; it is stated so a later change to the attribute
    /// allow-list cannot quietly turn this into a step that completes a reference for a client to follow.
    /// </remarks>
    public string Sanitize(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        return this.sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreatePolicy()
    {
        var policy = new HtmlSanitizer();

        Replace(policy.AllowedTags, AllowedElements);
        Replace(policy.AllowedAttributes, AllowedAttributes);

        // Cleared rather than narrowed. Style is where a body hides a reference behind url(), an at-rule imports one,
        // and a data attribute smuggles one past an attribute allow-list; none of the three is worth a single
        // declaration of formatting to a reader that wants the words.
        policy.AllowedCssProperties.Clear();
        policy.AllowedAtRules.Clear();
        policy.AllowedSchemes.Clear();
        policy.AllowDataAttributes = false;

        return policy;
    }

    private static void Replace(ISet<string> allowList, IEnumerable<string> allowed)
    {
        allowList.Clear();
        allowList.UnionWith(allowed);
    }
}

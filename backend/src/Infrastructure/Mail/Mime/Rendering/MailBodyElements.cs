// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Which elements the reduction drops outright, and what the ones it keeps mean.</summary>
/// <remarks>
/// <para>
/// The reduction produces a closed tree rather than markup, so an element it does not recognize can only ever
/// contribute words: there is no member of the document contract that carries a capability, and unwrapping an unknown
/// element therefore adds nothing but text. That is why the general answer here is to unwrap rather than to refuse, and
/// why the list that is refused is short and specific.
/// </para>
/// <para>
/// What is refused is refused <em>with its content</em>, which is the same choice the strict sanitizer makes and for
/// the same reason: unwrapping a <c>script</c> would keep the text it carries, which is inert and indistinguishable
/// from the message's own words. <c>template</c> and <c>noscript</c> are here beside it because they are where a parser
/// and a browser have historically disagreed about what a document is, and <c>svg</c> and <c>math</c> because foreign
/// content is the other half of that same disagreement.
/// </para>
/// </remarks>
internal static class MailBodyElements
{
    /// <summary>The elements whose content is dropped with them.</summary>
    internal static IReadOnlySet<string> Dropped { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "script",
        "style",
        "template",
        "noscript",
        "iframe",
        "frame",
        "frameset",
        "object",
        "embed",
        "applet",
        "form",
        "input",
        "button",
        "select",
        "option",
        "optgroup",
        "textarea",
        "label",
        "fieldset",
        "legend",
        "datalist",
        "output",
        "progress",
        "meter",
        "svg",
        "math",
        "base",
        "meta",
        "link",
        "title",
        "head",
        "audio",
        "video",
        "source",
        "track",
        "canvas",
        "map",
        "area",
        "dialog",
        "slot",
    };

    /// <summary>The elements that add emphasis to whatever their content inherited.</summary>
    internal static IReadOnlyDictionary<string, MailTextEmphasis> Emphasizing { get; } =
        new Dictionary<string, MailTextEmphasis>(StringComparer.OrdinalIgnoreCase)
        {
            ["b"] = MailTextEmphasis.Bold,
            ["strong"] = MailTextEmphasis.Bold,
            ["i"] = MailTextEmphasis.Italic,
            ["em"] = MailTextEmphasis.Italic,
            ["cite"] = MailTextEmphasis.Italic,
            ["dfn"] = MailTextEmphasis.Italic,
            ["var"] = MailTextEmphasis.Italic,
            ["u"] = MailTextEmphasis.Underline,
            ["ins"] = MailTextEmphasis.Underline,
            ["s"] = MailTextEmphasis.Strikethrough,
            ["strike"] = MailTextEmphasis.Strikethrough,
            ["del"] = MailTextEmphasis.Strikethrough,
            ["code"] = MailTextEmphasis.Monospace,
            ["kbd"] = MailTextEmphasis.Monospace,
            ["samp"] = MailTextEmphasis.Monospace,
            ["tt"] = MailTextEmphasis.Monospace,
        };

    /// <summary>The elements that begin a block, so a run of text before one is a paragraph of its own.</summary>
    /// <remarks>
    /// It exists because a message routinely writes its paragraphs as <c>div</c>s with no <c>p</c> anywhere, and a
    /// reduction that treated a <c>div</c> as inline would run a whole newsletter together into one paragraph.
    /// </remarks>
    internal static IReadOnlySet<string> Breaking { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "article",
        "aside",
        "center",
        "dd",
        "div",
        "dl",
        "dt",
        "figcaption",
        "figure",
        "footer",
        "header",
        "main",
        "nav",
        "p",
        "section",
    };
}

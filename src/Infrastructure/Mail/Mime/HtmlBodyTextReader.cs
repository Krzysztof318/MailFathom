// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;
using MimeKit.Text;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Derives readable text from an HTML message body.</summary>
/// <remarks>
/// <para>
/// The derivation is deliberately a tokenizer loop rather than a document model. Nothing here resolves a URL, loads a
/// style sheet, follows an <c>src</c>, or expands an external entity, so an HTML body cannot make extraction reach the
/// network or the filesystem no matter what it declares. A tokenizer also cannot be steered by malformed markup into
/// anything worse than a poorer reading of the same bytes.
/// </para>
/// <para>
/// MimeKit ships the tokenizer with MailKit, so deriving text here adds no dependency. The sanitizer beside it brings an
/// AngleSharp stack of its own, and keeping this reading on MimeKit means the two never have to agree on a version.
/// </para>
/// </remarks>
internal static class HtmlBodyTextReader
{
    /// <summary>Elements whose content is machinery rather than words a reader sees.</summary>
    /// <remarks>
    /// Exactly three, and each of them earns its place twice over: its content is never displayed, and HTML requires it
    /// to be closed. Suppression has to end on an end tag, so a void element such as <c>&lt;meta&gt;</c> — which never
    /// gets one — would swallow the rest of a body that wrote it outside a head, and an element whose end tag HTML lets
    /// an author omit, <c>&lt;head&gt;</c> among them, would do the same whenever it was omitted. Neither loses
    /// anything by being left out: a void element emits no content, and a head's own children are either handled here
    /// or emit nothing themselves.
    /// </remarks>
    private static readonly HtmlTagId[] NonRenderedElements =
    [
        HtmlTagId.Script,
        HtmlTagId.Style,
        HtmlTagId.Title,
    ];

    /// <summary>Elements that start a new line of text, whichever end of them is met.</summary>
    private static readonly HtmlTagId[] LineBreakingElements =
    [
        HtmlTagId.Address, HtmlTagId.Article, HtmlTagId.Aside, HtmlTagId.BlockQuote, HtmlTagId.Body,
        HtmlTagId.Br, HtmlTagId.Caption, HtmlTagId.Center, HtmlTagId.DD, HtmlTagId.Details, HtmlTagId.Dialog,
        HtmlTagId.Dir, HtmlTagId.Div, HtmlTagId.DL, HtmlTagId.DT, HtmlTagId.FieldSet, HtmlTagId.FigCaption,
        HtmlTagId.Figure, HtmlTagId.Footer, HtmlTagId.Form, HtmlTagId.H1, HtmlTagId.H2, HtmlTagId.H3,
        HtmlTagId.H4, HtmlTagId.H5, HtmlTagId.H6, HtmlTagId.Header, HtmlTagId.HR, HtmlTagId.LI,
        HtmlTagId.Listing, HtmlTagId.Main, HtmlTagId.Menu, HtmlTagId.Nav, HtmlTagId.OL, HtmlTagId.P,
        HtmlTagId.Pre, HtmlTagId.Section, HtmlTagId.Summary, HtmlTagId.Table, HtmlTagId.TBody, HtmlTagId.TD,
        HtmlTagId.TextArea, HtmlTagId.Tfoot, HtmlTagId.TH, HtmlTagId.THead, HtmlTagId.TR, HtmlTagId.UL,
        HtmlTagId.Xmp,
    ];

    /// <summary>Reads the words an HTML body displays, with its block structure reduced to line breaks.</summary>
    /// <param name="html">The HTML body source.</param>
    /// <param name="maxCharacters">The greatest number of characters to read out of the markup.</param>
    /// <returns>The derived text, which is empty when the body displayed nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="html" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCharacters" /> is not positive.</exception>
    /// <remarks>
    /// The bound stops the tokenizer rather than trimming what it produced, so the work a crafted body can demand is
    /// proportional to the bound instead of to the markup. It applies to the text as read, before the whitespace a
    /// document's layout carries is collapsed, so heavily formatted markup yields somewhat less than the bound.
    /// </remarks>
    public static string ReadDisplayedText(string html, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        using var htmlReader = new StringReader(html);
        var tokenizer = new HtmlTokenizer(htmlReader)
        {
            // Stated rather than inherited: the derived text is what a reader would see, so "&amp;" must become "&"
            // and a message that writes its whole body as character references must not be indexed as entity source.
            DecodeCharacterReferences = true,
        };

        var derivedText = new StringBuilder();
        var suppressedElement = (HtmlTagId?)null;

        while (derivedText.Length < maxCharacters && tokenizer.ReadNextToken(out var token))
        {
            switch (token)
            {
                case HtmlTagToken tag:
                    suppressedElement = ApplyTag(derivedText, tag, suppressedElement);
                    break;

                case HtmlDataToken data when suppressedElement is null:
                    derivedText.Append(data.Data);
                    break;
            }
        }

        return NormalizeWhitespace(derivedText.ToString());
    }

    /// <summary>Applies one tag's effect on the derived text and reports which element is now suppressing content.</summary>
    /// <remarks>
    /// Suppression tracks a single element rather than a stack, because the elements it covers do not nest inside one
    /// another in any body a mail client produces, and an unclosed one would otherwise swallow the rest of the message.
    /// Its end tag lifts it whichever element the tokenizer reports next.
    /// </remarks>
    private static HtmlTagId? ApplyTag(StringBuilder derivedText, HtmlTagToken tag, HtmlTagId? suppressedElement)
    {
        if (suppressedElement is { } suppressed)
        {
            return tag.IsEndTag && tag.Id == suppressed ? null : suppressedElement;
        }

        if (!tag.IsEndTag && !tag.IsEmptyElement && NonRenderedElements.Contains(tag.Id))
        {
            return tag.Id;
        }

        if (LineBreakingElements.Contains(tag.Id))
        {
            derivedText.Append('\n');
        }

        return null;
    }

    /// <summary>Reduces the whitespace HTML source carries to the line structure the markup expressed.</summary>
    /// <remarks>
    /// Source line breaks and indentation are layout of the markup rather than of the text, so they collapse to single
    /// spaces; only the breaks the elements themselves introduced survive. Runs of blank lines collapse to one, which
    /// keeps a paragraph boundary readable without letting a body of empty table rows become mostly newlines.
    /// </remarks>
    private static string NormalizeWhitespace(string derivedText)
    {
        var lines = derivedText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

        var normalized = new StringBuilder();
        var blankLineIsPending = false;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blankLineIsPending = normalized.Length > 0;

                continue;
            }

            if (blankLineIsPending)
            {
                normalized.Append('\n');
                blankLineIsPending = false;
            }

            if (normalized.Length > 0)
            {
                normalized.Append('\n');
            }

            normalized.Append(line);
        }

        return normalized.ToString();
    }
}

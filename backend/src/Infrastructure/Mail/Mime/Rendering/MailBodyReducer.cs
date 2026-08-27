// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Reduces one parsed mail body to the closed document tree a reading pane draws natively.</summary>
/// <remarks>
/// <para>
/// The reduction is a walk that produces typed values rather than a pass that removes things from markup, and that
/// difference is the security argument. Nothing here copies an element, an attribute, or a declaration through: every
/// member of the result is constructed from a value this file recognized, so a construct nobody thought about
/// contributes words at most. There is no output in which a script, a handler, a frame, or an embedded object could
/// survive, because the contract has nowhere to put one.
/// </para>
/// <para>
/// Remote references are dropped while the tree is built rather than marked for a renderer to abstain from, so a
/// rendering defect downstream cannot leak by fetching: the address is not in the document. What the reader is told
/// instead is how many were removed. The one exception is the picture the reader asked for by name, and it widens
/// exactly one thing — <c>http</c> and <c>https</c> on a picture's source — because a document handed to a pane fetches
/// what it carries and what it carries is therefore the whole of the control.
/// </para>
/// <para>
/// Every bound is a bound on hostile input, and reaching one truncates rather than refuses: half a newsletter is worth
/// more to a reader than none of it, and the document says which it got.
/// </para>
/// </remarks>
internal sealed class MailBodyReducer
{
    private readonly MailInlineImages inlineImages;
    private readonly bool retainRemoteImages;

    private int emittedBlocks;
    private int removedRemoteReferences;
    private int retainedRemoteImages;
    private bool truncated;

    /// <summary>Initializes a reduction.</summary>
    /// <param name="bounds">What one reduction may produce at most.</param>
    /// <param name="inlineImages">The pictures the message carries in itself, already resolved.</param>
    /// <param name="retainRemoteImages">Whether the reader asked for this message's remote pictures.</param>
    /// <exception cref="ArgumentNullException">Thrown when either reference argument is <see langword="null" />.</exception>
    internal MailBodyReducer(MailDocumentBounds bounds, MailInlineImages inlineImages, bool retainRemoteImages)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(inlineImages);

        this.Bounds = bounds;
        this.inlineImages = inlineImages;
        this.retainRemoteImages = retainRemoteImages;
    }

    /// <summary>Reduces the body of one parsed document.</summary>
    /// <param name="body">The parsed body element.</param>
    /// <returns>The document, which reports itself as refused when the body yielded no block.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="body" /> is <see langword="null" />.</exception>
    internal MailDocument Reduce(IElement body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var blocks = this.ReduceChildren(body, MailReductionContext.Root);

        return MailDocument.Reduced(
            blocks,
            this.removedRemoteReferences,
            this.retainedRemoteImages,
            this.inlineImages.ResolvedCount,
            this.inlineImages.UndrawnCount,
            this.truncated);
    }

    private List<MailDocumentBlock> ReduceChildren(INode parent, MailReductionContext context)
    {
        var blocks = new List<MailDocumentBlock>();
        var pending = new List<MailInlineRun>();

        foreach (var child in parent.ChildNodes)
        {
            this.ReduceNode(child, context, blocks, pending);
        }

        this.Flush(blocks, pending, context);

        return blocks;
    }

    private void ReduceNode(
        INode node,
        MailReductionContext context,
        List<MailDocumentBlock> blocks,
        List<MailInlineRun> pending)
    {
        switch (node)
        {
            case IText text:
                AppendText(pending, text.Data, context);

                break;

            case IElement element:
                this.ReduceElement(element, context, blocks, pending);

                break;

            default:
                // A comment, a processing instruction, or a document type carries nothing a reader is shown.
                break;
        }
    }

    private void ReduceElement(
        IElement element,
        MailReductionContext context,
        List<MailDocumentBlock> blocks,
        List<MailInlineRun> pending)
    {
        // The parser lower-cases the tag names of HTML content as it builds the tree, so the local name is already
        // the form the tables below are written in. The sets are case-insensitive anyway, which is what keeps a name
        // arriving in some other form classified rather than merely unrecognized.
        var name = element.LocalName;

        if (MailBodyElements.Dropped.Contains(name))
        {
            return;
        }

        var style = MailStyleReader.Read(element);
        if (style.Hidden)
        {
            return;
        }

        this.NoteRemoteReferences(element);

        if (context.Depth >= this.Bounds.MaximumDepth)
        {
            this.truncated = true;

            return;
        }

        var inside = context.Inside(style);

        switch (name)
        {
            case "br":
                pending.Add(new MailInlineRun("\n", context.Emphasis, context.Foreground, context.Link));

                break;

            case "img":
                this.Flush(blocks, pending, context);
                this.EmitImage(element, inside, blocks);

                break;

            case "a":
                this.ReduceAnchor(element, inside, blocks, pending);

                break;

            case "hr":
                this.Flush(blocks, pending, context);
                this.Emit(blocks, new MailSeparatorBlock());

                break;

            case "pre":
                this.Flush(blocks, pending, context);
                this.EmitPreformatted(element, blocks);

                break;

            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                this.Flush(blocks, pending, context);
                this.EmitHeading(element, name, inside, blocks);

                break;

            case "ul" or "ol":
                this.Flush(blocks, pending, context);
                this.EmitList(element, name == "ol", inside, blocks);

                break;

            case "table":
                this.Flush(blocks, pending, context);
                this.EmitTable(element, inside, blocks);

                break;

            case "blockquote":
                this.Flush(blocks, pending, context);
                this.EmitQuote(element, inside, blocks);

                break;

            default:
                this.ReduceOrdinary(element, name, context, inside, blocks, pending);

                break;
        }
    }

    /// <summary>Reduces an element that is neither a block of its own nor a dropped one.</summary>
    /// <remarks>
    /// The two cases differ only in whether a run of text before the element belongs to the same paragraph. An unknown
    /// element takes the inline answer, which is what makes an unrecognized tag contribute its words rather than lose
    /// them.
    /// </remarks>
    private void ReduceOrdinary(
        IElement element,
        string name,
        MailReductionContext context,
        MailReductionContext inside,
        List<MailDocumentBlock> blocks,
        List<MailInlineRun> pending)
    {
        var emphasized = MailBodyElements.Emphasizing.TryGetValue(name, out var emphasis)
            ? inside with { Emphasis = inside.Emphasis | emphasis }
            : inside;

        if (MailBodyElements.Breaking.Contains(name))
        {
            this.Flush(blocks, pending, context);
            this.EmitRange(blocks, this.ReduceChildren(element, emphasized));

            return;
        }

        foreach (var child in element.ChildNodes)
        {
            this.ReduceNode(child, emphasized, blocks, pending);
        }
    }

    /// <summary>Reduces an anchor, which is a link over words, over a picture, or over both.</summary>
    private void ReduceAnchor(
        IElement element,
        MailReductionContext inside,
        List<MailDocumentBlock> blocks,
        List<MailInlineRun> pending)
    {
        var link = MailLinkReader.Read(element.GetAttribute("href"), DisplayTextOf(element));
        var linked = inside with { Link = link ?? inside.Link };

        foreach (var child in element.ChildNodes)
        {
            this.ReduceNode(child, linked, blocks, pending);
        }
    }

    private void EmitImage(IElement element, MailReductionContext context, List<MailDocumentBlock> blocks)
    {
        if (this.ResolveImageSource(element.GetAttribute("src")) is not { } source)
        {
            return;
        }

        var image = new MailInlineImage(
            source,
            Bounded(element.GetAttribute("alt")),
            DimensionOf(element.GetAttribute("width")),
            DimensionOf(element.GetAttribute("height")));

        this.Emit(blocks, new MailImageBlock(image, context.Link, context.Alignment));
    }

    /// <summary>Resolves what a picture's source points at, or reports that nothing is drawn for it.</summary>
    /// <remarks>
    /// The four answers are the whole of what a source may be. A content identifier resolves to the message's own part;
    /// a <c>data:</c> URI the message wrote is kept when it is a picture this pane draws; an absolute address is kept
    /// only where the reader asked for remote content and counted as removed otherwise; and anything else — a relative
    /// reference above all — resolves to nothing, because there is no base to complete it against that would mean
    /// anything.
    /// </remarks>
    private string? ResolveImageSource(string? source)
    {
        if (source is not { Length: > 0 })
        {
            return null;
        }

        var reference = source.Trim();

        if (reference.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            return this.inlineImages.Resolve(reference);
        }

        if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return MailDataUri.Drawable(reference, this.Bounds.MaximumInlineImageOctets);
        }

        if (!Uri.TryCreate(reference, UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        if (!this.retainRemoteImages)
        {
            this.removedRemoteReferences++;

            return null;
        }

        this.retainedRemoteImages++;

        return absolute.AbsoluteUri;
    }

    private void EmitPreformatted(IElement element, List<MailDocumentBlock> blocks)
    {
        var text = Cut(element.TextContent, this.Bounds.MaximumCharactersPerRun);

        if (text.Trim().Length > 0)
        {
            this.Emit(blocks, new MailPreformattedBlock(text));
        }
    }

    private void EmitHeading(
        IElement element,
        string name,
        MailReductionContext context,
        List<MailDocumentBlock> blocks)
    {
        var content = this.ReduceInline(element, context);

        if (content.Count > 0)
        {
            this.Emit(
                blocks,
                new MailHeadingBlock(
                    int.Parse(name.AsSpan(1), CultureInfo.InvariantCulture),
                    content,
                    context.Alignment));
        }
    }

    private void EmitList(
        IElement element,
        bool ordered,
        MailReductionContext context,
        List<MailDocumentBlock> blocks)
    {
        var items = element.Children
            .Where(child => string.Equals(child.LocalName, "li", StringComparison.OrdinalIgnoreCase))
            .Select(item => new MailListItem(this.ReduceChildren(item, context.Inside(MailStyleReader.Read(item)))))
            .Where(item => item.Blocks.Count > 0)
            .ToArray();

        if (items.Length > 0)
        {
            this.Emit(blocks, new MailListBlock(ordered, items));
        }
    }

    private void EmitQuote(IElement element, MailReductionContext context, List<MailDocumentBlock> blocks)
    {
        var quoted = context with { QuoteDepth = context.QuoteDepth + 1 };
        var content = this.ReduceChildren(element, quoted);

        if (content.Count > 0)
        {
            this.Emit(blocks, new MailQuoteBlock(quoted.QuoteDepth, content));
        }
    }

    private void EmitTable(IElement element, MailReductionContext context, List<MailDocumentBlock> blocks)
    {
        var table = MailTableReducer.Reduce(element, context, this);

        if (table is not null)
        {
            this.Emit(blocks, table);
        }
    }

    /// <summary>Reduces an element that the contract holds as words rather than as blocks.</summary>
    /// <remarks>
    /// A heading is the case, and a heading holding a <c>div</c> is the reason this does not simply keep the runs it
    /// gathered: the walk would have flushed those words into a block, and returning only what was still pending would
    /// drop them. So whatever became a block is read back for its own runs, and the heading keeps every word.
    /// </remarks>
    internal IReadOnlyList<MailInlineRun> ReduceInline(IElement element, MailReductionContext context)
    {
        var blocks = new List<MailDocumentBlock>();
        var pending = new List<MailInlineRun>();

        foreach (var child in element.ChildNodes)
        {
            this.ReduceNode(child, context, blocks, pending);
        }

        this.Flush(blocks, pending, context);

        return Merged([.. blocks.SelectMany(RunsOf)]);
    }

    private static IEnumerable<MailInlineRun> RunsOf(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => paragraph.Content,
        MailHeadingBlock heading => heading.Content,
        MailPreformattedBlock preformatted =>
            [new MailInlineRun(preformatted.Text, MailTextEmphasis.Monospace, null, null)],
        _ => [],
    };

    /// <summary>Reduces everything one element holds, which a table cell and a list item both need.</summary>
    internal List<MailDocumentBlock> ReduceBlocks(IElement element, MailReductionContext context) =>
        this.ReduceChildren(element, context);

    /// <summary>Gets what this reduction may produce at most, which the table reduction is held to as well.</summary>
    internal MailDocumentBounds Bounds { get; }

    /// <summary>Notes that the walk stopped at a bound rather than at the end of the body.</summary>
    internal void NoteTruncated() => this.truncated = true;

    /// <summary>Counts the references to somebody else's server that the document will not carry.</summary>
    /// <remarks>
    /// They are counted where they are met rather than where they would have been used, because none of them is ever
    /// used: a declaration block's <c>url()</c> and an element's <c>background</c> attribute have no member of the
    /// contract to reach, so the count is the only thing about them that survives. It is what lets a pane say that a
    /// message asked to load something rather than leave the reader wondering why it looks bare.
    /// </remarks>
    internal void NoteRemoteReferences(IElement element)
    {
        if (element.GetAttribute("style") is { } declarations
            && declarations.Contains("url(", StringComparison.OrdinalIgnoreCase))
        {
            this.removedRemoteReferences++;
        }

        if (element.GetAttribute("background") is { Length: > 0 } background
            && Uri.TryCreate(background.Trim(), UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            this.removedRemoteReferences++;
        }
    }

    private void Flush(List<MailDocumentBlock> blocks, List<MailInlineRun> pending, MailReductionContext context)
    {
        var merged = Merged(pending);

        pending.Clear();

        if (merged.Count > 0)
        {
            this.Emit(blocks, new MailParagraphBlock(merged, context.Alignment));
        }
    }

    private void Emit(List<MailDocumentBlock> blocks, MailDocumentBlock block)
    {
        if (this.emittedBlocks >= this.Bounds.MaximumBlocks)
        {
            this.truncated = true;

            return;
        }

        this.emittedBlocks++;
        blocks.Add(block);
    }

    private void EmitRange(List<MailDocumentBlock> blocks, IEnumerable<MailDocumentBlock> produced)
    {
        // Already counted as they were emitted into the nested list, so they are moved rather than emitted again.
        blocks.AddRange(produced);
    }

    /// <summary>Appends one text node's words to the paragraph being built.</summary>
    /// <remarks>
    /// Whitespace is collapsed exactly as a browser collapses it, because a message's markup is written expecting that:
    /// the newlines and indentation between tags are the author's formatting of their own source rather than something
    /// the reader is meant to see. A run of whitespace at the start of a paragraph is dropped for the same reason.
    /// </remarks>
    private static void AppendText(List<MailInlineRun> pending, string data, MailReductionContext context)
    {
        var text = Collapse(data, pending.Count == 0);

        if (text.Length == 0)
        {
            return;
        }

        pending.Add(new MailInlineRun(text, context.Emphasis, context.Foreground, context.Link));
    }

    private static string Collapse(string data, bool atStart)
    {
        var collapsed = new StringBuilder(data.Length);
        var inWhitespace = atStart;

        foreach (var character in data)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!inWhitespace)
                {
                    collapsed.Append(' ');
                    inWhitespace = true;
                }

                continue;
            }

            collapsed.Append(character);
            inWhitespace = false;
        }

        return collapsed.ToString();
    }

    /// <summary>Joins the runs that are drawn identically and drops what a reader would see as nothing.</summary>
    /// <remarks>
    /// A body written as one span per word is ordinary in templated mail, and a document carrying it that way would
    /// cost a pane a text element per word. Joining is safe because a run carries only what it is drawn with, so two
    /// adjacent runs agreeing on all four members are one run by construction.
    /// </remarks>
    private static IReadOnlyList<MailInlineRun> Merged(List<MailInlineRun> pending)
    {
        var merged = new List<MailInlineRun>(pending.Count);

        foreach (var run in pending)
        {
            if (merged.Count > 0 && SameFormatting(merged[^1], run))
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };

                continue;
            }

            merged.Add(run);
        }

        if (merged.Count > 0)
        {
            merged[^1] = merged[^1] with { Text = merged[^1].Text.TrimEnd(' ') };
        }

        return [.. merged.Where(run => run.Text.Length > 0)];
    }

    private static bool SameFormatting(MailInlineRun left, MailInlineRun right) =>
        left.Emphasis == right.Emphasis
        && left.Foreground == right.Foreground
        && Equals(left.Link, right.Link);

    /// <summary>Reads the words an anchor shows, which is what its target is judged against.</summary>
    private static string DisplayTextOf(IElement element) =>
        Collapse(Cut(element.TextContent, 1024), atStart: true).Trim();

    private static string? Bounded(string? text) =>
        text is null ? null : Cut(Collapse(text, atStart: true).Trim(), 1024);

    private static string Cut(string text, int maximumCharacters) =>
        text.Length <= maximumCharacters ? text : text[..maximumCharacters];

    /// <summary>Reads a pixel dimension a picture asked for, which a pane uses for the shape rather than the size.</summary>
    private static int? DimensionOf(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var digits = value.Trim();
        if (digits.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            digits = digits[..^2].Trim();
        }

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels)
            && pixels is > 0 and <= 10_000
            ? pixels
            : null;
    }
}

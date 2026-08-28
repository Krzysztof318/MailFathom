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
    /// <summary>The longest anchor text a target is judged against, past which the words name no place anyway.</summary>
    private const int MaximumComparedTextLength = 1024;

    /// <summary>How deeply the anchor's own text is gathered, so a nested body cannot cost the walk its stack.</summary>
    private const int MaximumComparedDepth = 32;

    /// <summary>How many elements of a subtree nobody is shown are still read for what they would have loaded.</summary>
    /// <remarks>
    /// The walk stops at a hidden element, so that subtree is read here instead of descended into, and a message could
    /// otherwise make the reading arbitrarily large. Past the bound the count understates rather than misreports, and
    /// the document says it was truncated.
    /// </remarks>
    private const int MaximumHiddenElementsCounted = 512;

    private readonly MailInlineImages inlineImages;
    private readonly bool retainRemoteImages;

    private int emittedBlocks;
    private int removedRemoteReferences;
    private int retainedRemoteImages;
    private int undrawnPictures;
    private long emittedImageOctets;
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

        // The body element never reaches the element walk, so what that walk counts about any other element is counted
        // here. A background attribute or a background image on the body itself is how mail has put a picture behind a
        // whole message for twenty years, and leaving it uncounted would tell the reader the message asked to load
        // nothing when it asked to load something.
        this.NoteRemoteReferences(body);

        var blocks = this.ReduceChildren(body, MailReductionContext.Root);

        return MailDocument.Reduced(
            blocks,
            this.removedRemoteReferences,
            this.retainedRemoteImages,
            this.inlineImages.ResolvedCount,
            this.inlineImages.UndrawnCount + this.undrawnPictures,
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
                this.AppendText(pending, text.Data, context);

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
            this.NoteHiddenReferences(element);

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
        if (this.ResolveImageSource(element.GetAttribute("src")) is not { } source || !this.Affords(source))
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

    /// <summary>Charges what a picture would add to the answer, and refuses the one that would carry it past the bound.</summary>
    /// <remarks>
    /// The bound is on what the document carries rather than on what was decoded, because those are not the same
    /// number: one part is resolved once and every reference naming it emits the whole encoding again, so a body
    /// repeating a single <c>cid:</c> reference composes an answer many times the size of the message it came from —
    /// past what a reading pane will accept, which loses the reader the words as well as the pictures. A remote address
    /// the reader consented to costs nothing here, because the document carries the address rather than the octets.
    /// </remarks>
    private bool Affords(string source)
    {
        var octets = MailDocumentImages.OctetsBehind(source);

        if (this.emittedImageOctets + octets > this.Bounds.MaximumInlineImageOctetsPerDocument)
        {
            this.undrawnPictures++;
            this.truncated = true;

            return false;
        }

        this.emittedImageOctets += octets;

        return true;
    }

    /// <summary>Resolves what a picture's source points at, or reports that nothing is drawn for it.</summary>
    /// <remarks>
    /// <para>
    /// The four answers are the whole of what a source may be. A content identifier resolves to the message's own part;
    /// a <c>data:</c> URI the message wrote is kept when it is a picture this pane draws; an absolute address is kept
    /// only where the reader asked for remote content and counted as removed otherwise; and anything else — a relative
    /// reference above all — resolves to nothing, because there is no base to complete it against that would mean
    /// anything.
    /// </para>
    /// <para>
    /// An absolute address is asked of the message's own parts before it is treated as remote, because a part may be
    /// reached by its <c>Content-Location</c> as well as by its content identifier. A message carrying the octets and
    /// naming them by location is self-contained, so fetching that address would turn a message that asks nothing of
    /// anybody into one that tells its sender it was opened.
    /// </para>
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

        if (this.inlineImages.Resolve(absolute.AbsoluteUri) is { } carried)
        {
            return carried;
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

    /// <summary>Emits a heading, which the contract holds as words and which may still hold something that is not.</summary>
    /// <remarks>
    /// The words are read back out of the blocks the walk produced rather than taken from what was left pending,
    /// because a heading holding a <c>div</c> has already had its words flushed into a block. A picture is the other
    /// thing a heading holds — a masthead logo is written exactly that way — and it becomes a block no reading of runs
    /// can express, so it is kept beside the heading instead of being dropped with the list it was gathered into.
    /// </remarks>
    private void EmitHeading(
        IElement element,
        string name,
        MailReductionContext context,
        List<MailDocumentBlock> blocks)
    {
        var gathered = new List<MailDocumentBlock>();
        var pending = new List<MailInlineRun>();

        foreach (var child in element.ChildNodes)
        {
            this.ReduceNode(child, context, gathered, pending);
        }

        this.Flush(gathered, pending, context);

        var content = this.Merged([.. gathered.SelectMany(RunsOf)]);

        if (content.Count > 0)
        {
            this.Emit(
                blocks,
                new MailHeadingBlock(
                    int.Parse(name.AsSpan(1), CultureInfo.InvariantCulture),
                    content,
                    context.Alignment));
        }

        this.EmitRange(blocks, gathered.Where(gatheredBlock => !IsWords(gatheredBlock)));
    }

    /// <summary>Reduces a list, whose items the ordinary element walk never reaches.</summary>
    /// <remarks>
    /// An item is read here rather than by the walk, so everything the walk does to an element it meets has to be done
    /// to it here too: an item that asked not to be drawn is dropped exactly as any other hidden element is, and the
    /// references to somebody else's server an item carries are counted exactly as any other element's are. Reading
    /// only the style it inherits, which is what this did, made a list the one place a message could put a hidden
    /// item and an uncounted tracking reference.
    /// </remarks>
    private void EmitList(
        IElement element,
        bool ordered,
        MailReductionContext context,
        List<MailDocumentBlock> blocks)
    {
        var items = new List<MailListItem>();

        foreach (var item in element.Children.Where(child =>
            string.Equals(child.LocalName, "li", StringComparison.OrdinalIgnoreCase)))
        {
            var style = MailStyleReader.Read(item);
            if (style.Hidden)
            {
                this.NoteHiddenReferences(item);

                continue;
            }

            this.NoteRemoteReferences(item);

            var reduced = this.ReduceChildren(item, context.Inside(style));
            if (reduced.Count > 0)
            {
                items.Add(new MailListItem(reduced));
            }
        }

        if (items.Count > 0)
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

    private static IEnumerable<MailInlineRun> RunsOf(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => paragraph.Content,
        MailHeadingBlock heading => heading.Content,
        MailPreformattedBlock preformatted =>
            [new MailInlineRun(preformatted.Text, MailTextEmphasis.Monospace, null, null)],
        _ => [],
    };

    /// <summary>Says whether a block is one <see cref="RunsOf" /> can express as words, which decides what is kept.</summary>
    private static bool IsWords(MailDocumentBlock block) =>
        block is MailParagraphBlock or MailHeadingBlock or MailPreformattedBlock;

    /// <summary>Reduces everything one element holds, which a table cell and a list item both need.</summary>
    internal List<MailDocumentBlock> ReduceBlocks(IElement element, MailReductionContext context) =>
        this.ReduceChildren(element, context);

    /// <summary>Gets what this reduction may produce at most, which the table reduction is held to as well.</summary>
    internal MailDocumentBounds Bounds { get; }

    /// <summary>Notes that the walk stopped at a bound rather than at the end of the body.</summary>
    internal void NoteTruncated() => this.truncated = true;

    /// <summary>Counts what a subtree nobody is shown would still have asked somebody's server for.</summary>
    /// <remarks>
    /// <para>
    /// A tracking pixel is a hidden picture, so dropping a hidden element without reading it told the reader the
    /// message asked to load nothing in exactly the case where the message was asking to load something. Nothing here
    /// is drawn and nothing is ever fetched; the count is the whole of what survives, as it is for every other removed
    /// reference.
    /// </para>
    /// <para>
    /// This is the one place a subtree is read rather than descended into, and it is reached only at the outermost
    /// hidden element — the walk never enters one — so a nested hidden element is read as part of its ancestor's
    /// reading rather than a second time.
    /// </para>
    /// </remarks>
    internal void NoteHiddenReferences(IElement element)
    {
        var unread = new Stack<IElement>();
        var counted = 0;

        unread.Push(element);

        while (unread.Count > 0)
        {
            var hidden = unread.Pop();

            if (++counted > MaximumHiddenElementsCounted)
            {
                this.truncated = true;

                break;
            }

            this.NoteRemoteReferences(hidden);
            this.NoteHiddenPicture(hidden);

            foreach (var child in hidden.Children)
            {
                unread.Push(child);
            }
        }
    }

    /// <summary>Counts the picture a hidden element names, which the emission that would have counted it never reaches.</summary>
    /// <remarks>
    /// A source the message carries in itself is not counted, because nothing is asked of anybody for it; nor is one
    /// the reader consented to, because a hidden picture is not drawn and so is not fetched either. What is left is a
    /// reference to somebody else's server that the document will not carry, which is what the count is about.
    /// </remarks>
    private void NoteHiddenPicture(IElement element)
    {
        if (!element.LocalName.Equals("img", StringComparison.OrdinalIgnoreCase)
            || element.GetAttribute("src") is not { Length: > 0 } source
            || !Uri.TryCreate(source.Trim(), UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            || this.inlineImages.Resolve(absolute.AbsoluteUri) is not null)
        {
            return;
        }

        this.removedRemoteReferences++;
    }

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
        var merged = this.Merged(pending);

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
    private void AppendText(List<MailInlineRun> pending, string data, MailReductionContext context)
    {
        var text = Collapse(data, pending.Count == 0);

        if (text.Length == 0)
        {
            return;
        }

        if (text.Length > this.Bounds.MaximumCharactersPerRun)
        {
            text = text[..this.Bounds.MaximumCharactersPerRun];
            this.truncated = true;
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
    /// <para>
    /// A body written as one span per word is ordinary in templated mail, and a document carrying it that way would
    /// cost a pane a text element per word. Joining is safe because a run carries only what it is drawn with, so two
    /// adjacent runs agreeing on all four members are one run by construction.
    /// </para>
    /// <para>
    /// This is where the two bounds a block's own words are held to are applied, because it is the last place the
    /// words of one block are all in hand. A join that would carry a run past the character bound is not taken — the
    /// two stay two runs saying the same thing — while a block whose runs reach the count bound stops there and says
    /// the document was truncated, since alternating formatting is exactly what defeats the join.
    /// </para>
    /// </remarks>
    private IReadOnlyList<MailInlineRun> Merged(List<MailInlineRun> pending)
    {
        var merged = new List<MailInlineRun>(pending.Count);

        foreach (var run in pending)
        {
            if (merged.Count > 0
                && SameFormatting(merged[^1], run)
                && merged[^1].Text.Length + run.Text.Length <= this.Bounds.MaximumCharactersPerRun)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + run.Text };

                continue;
            }

            if (merged.Count >= this.Bounds.MaximumRunsPerBlock)
            {
                this.truncated = true;

                break;
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
    /// <remarks>
    /// The words the reduction draws rather than every character the element holds. <c>TextContent</c> carries what a
    /// script, a style block, and an element styled out of the drawing say too, and a sender needs only one space
    /// among them to make the anchor's text stop reading as a host name — at which point there is nothing left to
    /// compare and a link claiming to be somebody's bank is reported as making no claim at all. So what the judgement
    /// is made from is what a reader is shown, which is what the claim was about in the first place.
    /// </remarks>
    private static string DisplayTextOf(IElement element)
    {
        var drawn = new StringBuilder();

        AppendDrawnText(element, drawn, depth: 0);

        return Collapse(drawn.ToString(), atStart: true).Trim();
    }

    /// <summary>Gathers the text a walk of this element would draw, skipping what the walk removes.</summary>
    private static void AppendDrawnText(IElement element, StringBuilder drawn, int depth)
    {
        if (depth >= MaximumComparedDepth)
        {
            return;
        }

        foreach (var child in element.ChildNodes)
        {
            if (drawn.Length >= MaximumComparedTextLength)
            {
                return;
            }

            switch (child)
            {
                case IText text:
                    drawn.Append(
                        text.Data.AsSpan(0, Math.Min(text.Data.Length, MaximumComparedTextLength - drawn.Length)));

                    break;

                case IElement nested
                    when !MailBodyElements.Dropped.Contains(nested.LocalName)
                        && !MailStyleReader.Read(nested).Hidden:
                    AppendDrawnText(nested, drawn, depth + 1);

                    break;

                default:
                    // A comment, a dropped element, and one styled out of the drawing all show a reader nothing.
                    break;
            }
        }
    }

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

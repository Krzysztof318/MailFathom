// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Reads every text a document holds, and writes a document back with those texts replaced.</summary>
/// <remarks>
/// <para>
/// It exists for one caller: the sensitive-content scan a read runs before mail leaves this deployment. The document is
/// the third representation of a body, so it has to be guarded exactly as the other two are — and a tree cannot be
/// handed to a scanner as one string, because a finding straddling the join between two runs would redact across words
/// that have nothing to do with each other.
/// </para>
/// <para>
/// So the two halves are separated and the order between them is the contract: <see cref="Collect" /> walks the tree in
/// reading order and <see cref="Rewrite" /> walks it again in the same order, taking the replacements positionally. A
/// caller therefore guards a flat list and gets a tree back, and nothing in between has to understand a block.
/// </para>
/// <para>
/// What is collected is what the message's author wrote: the words of every run, the text of every preformatted block,
/// and what a picture says it shows. A link's target is not among them, for the reason an address is not scanned
/// anywhere else here — it is a routing identity a reader acts on rather than free text, and redacting it would remove
/// the reader's ability to see where a link goes while protecting nothing the words did not already carry.
/// </para>
/// </remarks>
public static class MailDocumentTexts
{
    /// <summary>Reads every text the document holds, in reading order.</summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The texts, in the order <see cref="Rewrite" /> puts them back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<string> Collect(MailDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var collected = new List<string>();

        CollectBlocks(document.Blocks, collected);

        return collected;
    }

    /// <summary>Writes the document back with every text replaced by the one at its position.</summary>
    /// <param name="document">The document to rewrite.</param>
    /// <param name="texts">The replacements, in the order <see cref="Collect" /> produced them.</param>
    /// <returns>The rewritten document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the replacements do not number what the document holds.</exception>
    public static MailDocument Rewrite(MailDocument document, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(texts);

        var cursor = new TextCursor(texts);
        var blocks = RewriteBlocks(document.Blocks, cursor);

        if (!cursor.IsExhausted)
        {
            throw new ArgumentException(
                "The replacements do not number the texts the document holds.",
                nameof(texts));
        }

        return document with { Blocks = blocks };
    }

    private static void CollectBlocks(IReadOnlyList<MailDocumentBlock> blocks, List<string> collected)
    {
        foreach (var block in blocks)
        {
            CollectBlock(block, collected);
        }
    }

    private static void CollectBlock(MailDocumentBlock block, List<string> collected)
    {
        switch (block)
        {
            case MailParagraphBlock paragraph:
                CollectRuns(paragraph.Content, collected);

                break;

            case MailHeadingBlock heading:
                CollectRuns(heading.Content, collected);

                break;

            case MailListBlock list:
                foreach (var item in list.Items)
                {
                    CollectBlocks(item.Blocks, collected);
                }

                break;

            case MailTableBlock table:
                foreach (var cell in table.Rows.SelectMany(row => row.Cells))
                {
                    CollectBlocks(cell.Blocks, collected);
                }

                break;

            case MailQuoteBlock quote:
                CollectBlocks(quote.Blocks, collected);

                break;

            case MailImageBlock image:
                CollectImage(image.Image, collected);

                break;

            case MailPreformattedBlock preformatted:
                collected.Add(preformatted.Text);

                break;

            default:
                // A separator carries no text, and the hierarchy is closed, so nothing else reaches here.
                break;
        }
    }

    private static void CollectRuns(IReadOnlyList<MailInlineRun> runs, List<string> collected) =>
        collected.AddRange(runs.Select(run => run.Text));

    private static void CollectImage(MailInlineImage image, List<string> collected)
    {
        if (image.AlternativeText is { } alternativeText)
        {
            collected.Add(alternativeText);
        }
    }

    private static IReadOnlyList<MailDocumentBlock> RewriteBlocks(
        IReadOnlyList<MailDocumentBlock> blocks,
        TextCursor cursor) =>
        [.. blocks.Select(block => RewriteBlock(block, cursor))];

    private static MailDocumentBlock RewriteBlock(MailDocumentBlock block, TextCursor cursor) => block switch
    {
        MailParagraphBlock paragraph => new MailParagraphBlock(
            RewriteRuns(paragraph.Content, cursor),
            paragraph.Alignment),
        MailHeadingBlock heading => new MailHeadingBlock(
            heading.Level,
            RewriteRuns(heading.Content, cursor),
            heading.Alignment),
        MailListBlock list => new MailListBlock(
            list.Ordered,
            [.. list.Items.Select(item => new MailListItem(RewriteBlocks(item.Blocks, cursor)))]),
        MailTableBlock table => new MailTableBlock(table.Columns, RewriteRows(table.Rows, cursor)),
        MailQuoteBlock quote => new MailQuoteBlock(quote.Depth, RewriteBlocks(quote.Blocks, cursor)),
        MailImageBlock image => new MailImageBlock(RewriteImage(image.Image, cursor), image.Link, image.Alignment),
        MailPreformattedBlock preformatted => new MailPreformattedBlock(cursor.Next(preformatted.Text)),
        _ => block,
    };

    private static IReadOnlyList<MailTableRow> RewriteRows(IReadOnlyList<MailTableRow> rows, TextCursor cursor) =>
    [
        .. rows.Select(row => new MailTableRow(
            row.IsHeader,
            [
                .. row.Cells.Select(cell => cell with { Blocks = RewriteBlocks(cell.Blocks, cursor) }),
            ])),
    ];

    private static IReadOnlyList<MailInlineRun> RewriteRuns(IReadOnlyList<MailInlineRun> runs, TextCursor cursor) =>
        [.. runs.Select(run => run with { Text = cursor.Next(run.Text) })];

    private static MailInlineImage RewriteImage(MailInlineImage image, TextCursor cursor) =>
        image.AlternativeText is null
            ? image
            : image with { AlternativeText = cursor.Next(image.AlternativeText) };

    /// <summary>Hands out the replacements in the order the walk asks for them.</summary>
    /// <remarks>
    /// It carries the original as well, so a caller that handed over fewer texts than the document holds is told so at
    /// the end rather than silently rewriting the first few blocks and leaving the rest as they were.
    /// </remarks>
    private sealed class TextCursor(IReadOnlyList<string> texts)
    {
        private int position;

        internal bool IsExhausted { get => !field && this.position == texts.Count; private set; }

        internal string Next(string original)
        {
            if (this.position == texts.Count)
            {
                this.IsExhausted = true;

                return original;
            }

            return texts[this.position++];
        }
    }
}

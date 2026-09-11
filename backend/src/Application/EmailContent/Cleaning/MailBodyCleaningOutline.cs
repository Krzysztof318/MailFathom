// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document;
using MailFathom.Application.EmailContent.Rendering.Document.Blocks;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Cleaning;

/// <summary>Describes a reduced document as the outline a cleaning is decided from.</summary>
/// <remarks>
/// <para>
/// One line per top-level block, and deliberately nothing per nested block: what an answer may drop is a block of the
/// document's own reading order, so describing the paragraphs inside a table cell would offer a reach the answer has no
/// way to name. A nested block still contributes to the line above it — its words to the opening and its links to the
/// count — because that is what makes a wrapped footer recognizable as one.
/// </para>
/// <para>
/// The opening is bounded per block and the whole outline is bounded again by the plan's own request ceiling, for the
/// reason every bound here exists twice: a message of two hundred short paragraphs and a message of two enormous ones
/// both have to produce a turn the endpoint accepts.
/// </para>
/// </remarks>
public static class MailBodyCleaningOutline
{
    /// <summary>The greatest number of characters one block's opening contributes.</summary>
    /// <remarks>
    /// Enough to tell a legal clause from a sentence somebody wrote, and far short of what would make the outline a
    /// second copy of the body. Measured against a real corpus rather than chosen: the distinctions this pass is asked to
    /// make — a preheader, a category menu, a postal address, a confidentiality notice — are all recognizable from their
    /// first words.
    /// </remarks>
    public const int MaximumOpeningLength = 240;

    /// <summary>Describes one reduced document as an outline.</summary>
    /// <param name="document">The reduced document, whose top-level blocks are what an answer names.</param>
    /// <param name="subject">The subject the message carried, or <see langword="null" /> where it carried none.</param>
    /// <param name="senderName">The envelope sender, or <see langword="null" /> where the message named nobody.</param>
    /// <returns>The outline.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public static CleanableMailBody Describe(MailDocument document, string? subject, string? senderName)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new CleanableMailBody(
            subject,
            senderName,
            [
                .. document.Blocks.Select((block, index) => new CleanableMailBlock(
                    index,
                    block.Type.Identity,
                    LinksIn(block),
                    Opening(block))),
            ]);
    }

    /// <summary>Reads the start of what a block says, as one line with its own whitespace collapsed.</summary>
    /// <remarks>
    /// Collapsed rather than carried, because a block's line breaks are the sender's layout and a turn of one line per
    /// block is what makes the outline readable as a list. A preformatted block is the one place those breaks are the
    /// content, and it is collapsed here too: what this line is for is recognizing the block, not reproducing it.
    /// </remarks>
    private static string Opening(MailDocumentBlock block)
    {
        var collapsed = string.Join(' ', TextsIn(block).Where(text => text.Trim().Length > 0)).Trim();
        var folded = string.Join(' ', collapsed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return folded.Length <= MaximumOpeningLength
            ? folded
            : MailTextBounds.TruncateAtTextElementBoundary(folded, MaximumOpeningLength);
    }

    private static IEnumerable<string> TextsIn(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => paragraph.Content.Select(run => run.Text),
        MailHeadingBlock heading => heading.Content.Select(run => run.Text),
        MailPreformattedBlock preformatted => [preformatted.Text],
        MailImageBlock image => image.Image.AlternativeText is { } alternativeText ? [alternativeText] : [],
        MailListBlock list => list.Items.SelectMany(item => item.Blocks).SelectMany(TextsIn),
        MailTableBlock table => table.Rows
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Blocks)
            .SelectMany(TextsIn),
        MailQuoteBlock quote => quote.Blocks.SelectMany(TextsIn),
        _ => [],
    };

    /// <summary>Counts the links a block holds, which is the signal a menu and a row of social icons are recognized by.</summary>
    private static int LinksIn(MailDocumentBlock block) => block switch
    {
        MailParagraphBlock paragraph => paragraph.Content.Count(run => run.Link is not null),
        MailHeadingBlock heading => heading.Content.Count(run => run.Link is not null),
        MailImageBlock image => image.Link is null ? 0 : 1,
        MailListBlock list => list.Items.SelectMany(item => item.Blocks).Sum(LinksIn),
        MailTableBlock table => table.Rows
            .SelectMany(row => row.Cells)
            .SelectMany(cell => cell.Blocks)
            .Sum(LinksIn),
        MailQuoteBlock quote => quote.Blocks.Sum(LinksIn),
        _ => 0,
    };
}

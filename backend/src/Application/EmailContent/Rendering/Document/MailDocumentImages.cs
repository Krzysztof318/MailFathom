// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering.Document.Blocks;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Reads how many octets of its own pictures a document carries.</summary>
/// <remarks>
/// <para>
/// The document's own bounds hold what one message may inline. This reads what one message actually did, so the read
/// sequencing several messages can spend a budget across them — the same arrangement
/// <see cref="EmailContentRenderingBounds.RemainingCharactersForRead" /> already has for words, and for the same
/// reason: only the code sequencing the call knows what an earlier message already drew.
/// </para>
/// <para>
/// It is counted in octets rather than in characters because that is what the bound governing it is stated in, and
/// because a <c>data:</c> URI is a third longer than the picture behind it. Counting the encoding against a character
/// budget written for words would let one photograph starve the words of the next message in the batch.
/// </para>
/// </remarks>
public static class MailDocumentImages
{
    /// <summary>Reads the octets behind every picture the document carries in itself.</summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The octets, counting a remote reference the reader asked for as none, because it carries no octets.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    public static long OctetsIn(MailDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return OctetsInBlocks(document.Blocks);
    }

    private static long OctetsInBlocks(IReadOnlyList<MailDocumentBlock> blocks) =>
        blocks.Sum(OctetsInBlock);

    private static long OctetsInBlock(MailDocumentBlock block) => block switch
    {
        MailImageBlock image => OctetsBehind(image.Image.Source),
        MailListBlock list => list.Items.Sum(item => OctetsInBlocks(item.Blocks)),
        MailTableBlock table => table.Rows.SelectMany(row => row.Cells).Sum(cell => OctetsInBlocks(cell.Blocks)),
        MailQuoteBlock quote => OctetsInBlocks(quote.Blocks),
        _ => 0,
    };

    /// <summary>Reads how many octets a composed <c>data:</c> URI carries, and nothing for a source that is not one.</summary>
    /// <param name="source">What a picture block names as the place its octets come from.</param>
    /// <returns>The octets the source carries in itself, which is none for an address the answer only points at.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Public because the reduction composing a document has to charge a picture against the same bound this reads it
    /// back against, and one arithmetic answering both is what keeps the two numbers the same number.
    /// </remarks>
    public static long OctetsBehind(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var separator = source.IndexOf(',', StringComparison.Ordinal);

        return separator < 0 ? 0 : (long)(source.Length - separator - 1) * 3 / 4;
    }
}

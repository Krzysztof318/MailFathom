// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads the text of an Office Open XML document out of the archive it is packaged as.</summary>
/// <remarks>
/// <para>
/// The three Office Open XML formats are zip archives of XML parts, and the base class library reads both halves — so
/// this walks them directly rather than through a document model. That is not only the smaller dependency: a document
/// model inflates a part before handing it over, which is precisely the moment a decompression bomb has already won.
/// Reading the archive here is what makes <see cref="BoundedInflationStream" /> possible at all.
/// </para>
/// <para>
/// Only the parts that carry text are opened — for a word-processing document that is the body, its headers, its
/// footers, and its two note parts; for a deck and a workbook, the numbered page parts and the string table. A macro
/// project, an embedded object, an OLE package, an image, and every other part of the package are never read, never
/// decoded, and never handed to anything — extraction reads structure and text and evaluates nothing.
/// </para>
/// </remarks>
internal sealed partial class OpenXmlAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string WordDocumentPart = "word/document.xml";
    private const string SharedStringsPart = "xl/sharedStrings.xml";

    /// <summary>The parts a word-processing document keeps its notes in, read after the body and in this order.</summary>
    private static readonly string[] WordNoteParts = ["word/footnotes.xml", "word/endnotes.xml"];

    private readonly BoundedArchivePartReader parts = new(options);

    /// <summary>Reads one Office Open XML attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="format">Which of the three Office Open XML formats it is.</param>
    /// <param name="cancellationToken">Cancels the read between parts and between elements.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when a configured ceiling is crossed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the octets are not a readable archive, or the package declares none of the parts its format is read from.</exception>
    /// <exception cref="XmlException">Thrown when a part is not readable XML.</exception>
    public ExtractedAttachmentText Read(
        Stream content,
        AttachmentDocumentFormat format,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count > options.MaxContainerParts)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        var budget = new DecompressionBudget(options.MaxDecompressedOctets, content.Length);
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);

        return format switch
        {
            AttachmentDocumentFormat.WordOpenXml => this.ReadDocument(archive, budget, text, cancellationToken),
            AttachmentDocumentFormat.PresentationOpenXml => this.ReadPresentation(archive, budget, text, cancellationToken),
            AttachmentDocumentFormat.SpreadsheetOpenXml => this.ReadWorkbook(archive, budget, text, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not an Office Open XML package."),
        };
    }

    [GeneratedRegex(@"^ppt/slides/slide(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex SlidePartPattern();

    [GeneratedRegex(@"^word/header(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderPartPattern();

    [GeneratedRegex(@"^word/footer(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex FooterPartPattern();

    [GeneratedRegex(@"^xl/worksheets/sheet(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex WorksheetPartPattern();

    /// <summary>Reads a word-processing document, whose body is one page because the format records no pagination.</summary>
    /// <remarks>
    /// The body is not the whole of what somebody wrote. A letterhead's invoice number lives in a header part, a
    /// contract's terms often in footnotes, and each of those is a part of its own — so they are read after the body
    /// and in a fixed order, which is the honest arrangement available when the format records where a header prints
    /// rather than where its words belong in a reading. Everything else in the package stays unopened.
    /// </remarks>
    private ExtractedAttachmentText ReadDocument(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var document = archive.GetEntry(WordDocumentPart)
            ?? throw new InvalidDataException("The package declares no word-processing document part.");

        bool carriedText;

        using (var reader = this.parts.OpenPart(document, budget))
        {
            carriedText = this.ReadRunsInto(reader, WordprocessingNamespace, text, cancellationToken);
        }

        foreach (var part in SurroundingWordParts(archive))
        {
            text.EndLine();

            using var reader = this.parts.OpenPart(part, budget);

            carriedText |= this.ReadRunsInto(reader, WordprocessingNamespace, text, cancellationToken);
        }

        return new ExtractedAttachmentText(text.ToText(), PageCount: 1, carriedText ? [] : [1]);
    }

    /// <summary>Selects the header, footer, and note parts of a word-processing package, in the order they are read.</summary>
    private static List<ZipArchiveEntry> SurroundingWordParts(ZipArchive archive) =>
    [
        .. OrderedParts(archive, HeaderPartPattern()),
        .. OrderedParts(archive, FooterPartPattern()),
        .. WordNoteParts.Select(archive.GetEntry).OfType<ZipArchiveEntry>(),
    ];

    /// <summary>Reads a presentation, one page per slide in the order the slide parts are named.</summary>
    /// <remarks>
    /// Part-name order rather than the order the deck presents in. A presentation records that in <c>sldIdLst</c>,
    /// resolved through the package's relationships, and reordering or deleting a slide leaves the part names where
    /// they were — so a reordered deck reads back in the wrong order and the pages named as carrying no text are named
    /// by their part number. Resolving the declared order is issue #1682.
    /// </remarks>
    private ExtractedAttachmentText ReadPresentation(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var slides = OrderedParts(archive, SlidePartPattern());

        if (slides.Count == 0)
        {
            throw new InvalidDataException("The package declares no slide part.");
        }

        var slidesWithoutText = new List<int>();

        foreach (var (slide, index) in slides.Select((slide, index) => (slide, index)))
        {
            bool carriedText;

            using (var reader = this.parts.OpenPart(slide, budget))
            {
                carriedText = this.ReadRunsInto(reader, DrawingNamespace, text, cancellationToken);
            }

            if (!carriedText)
            {
                slidesWithoutText.Add(index + 1);
            }

            text.EndLine();
        }

        return new ExtractedAttachmentText(text.ToText(), slides.Count, slidesWithoutText);
    }

    /// <summary>Reads a workbook, one page per worksheet in part-name order, resolving the table each cell indexes into.</summary>
    /// <remarks>
    /// <para>
    /// The order is the part names' rather than the workbook's own, which it records in the <c>sheets</c> element of
    /// <c>xl/workbook.xml</c>; a reordered or partly deleted workbook therefore reads back in the wrong order, and
    /// resolving the declared one is issue #1682.
    /// </para>
    /// <para>
    /// The string table is resolved rather than emitted whole. Emitting it would produce every word in the workbook
    /// attached to no sheet, which reads as text and says nothing about where the text is — and a sheet holding only a
    /// picture would be indistinguishable from one whose words are in the table.
    /// </para>
    /// </remarks>
    private ExtractedAttachmentText ReadWorkbook(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var sharedStrings = this.ReadSharedStrings(archive, budget, cancellationToken);
        var sheets = OrderedParts(archive, WorksheetPartPattern());

        if (sheets.Count == 0)
        {
            throw new InvalidDataException("The package declares no worksheet part.");
        }

        var sheetsWithoutText = new List<int>();

        foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index)))
        {
            bool carriedText;

            using (var reader = this.parts.OpenPart(sheet, budget))
            {
                carriedText = this.ReadCellsInto(reader, sharedStrings, text, cancellationToken);
            }

            if (!carriedText)
            {
                sheetsWithoutText.Add(index + 1);
            }

            text.EndLine();
        }

        return new ExtractedAttachmentText(text.ToText(), sheets.Count, sheetsWithoutText);
    }

    /// <summary>Collects the runs of text in one part, ending a line where the format ends a paragraph.</summary>
    /// <returns><see langword="true" /> when the part carried anything but whitespace; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// Word-processing and presentation markup differ only in the namespace their text runs are written in, which is
    /// why one walk serves both: <c>t</c> holds the characters and <c>p</c> is what separates them into lines. The
    /// answer is what the part itself carried rather than how long the gathered text grew, because a line break written
    /// between two pages would otherwise read as the second page having said something.
    /// </para>
    /// <para>
    /// A tab and a line break are elements rather than characters here exactly as they are in OpenDocument, so they are
    /// read as the whitespace they stand for — otherwise the invoice line, the table of contents, and the form field
    /// that a tab separates all come back as one joined word.
    /// </para>
    /// <para>
    /// Everything inside a paragraph-properties element is skipped, because that is where the same names mean
    /// something else: <c>tab</c> under <c>pPr</c> declares a tab <em>stop</em> and stands in for no character at all,
    /// so reading it would put a space into every paragraph that overrides the default stops.
    /// </para>
    /// </remarks>
    private bool ReadRunsInto(
        XmlReader reader,
        string textNamespace,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var insideRun = false;
        var propertiesDepth = -1;
        var carriedText = false;

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (propertiesDepth >= 0)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == propertiesDepth)
                {
                    propertiesDepth = -1;
                }

                continue;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == textNamespace:
                    ReadRunElement(reader, text, ref insideRun, ref propertiesDepth);
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (insideRun)
                    {
                        text.Add(reader.Value);
                        carriedText |= !string.IsNullOrWhiteSpace(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement when reader.NamespaceURI == textNamespace:
                    if (reader.LocalName == "t")
                    {
                        insideRun = false;
                    }
                    else if (reader.LocalName == "p")
                    {
                        text.EndLine();
                    }

                    break;

                default:
                    break;
            }
        }

        return carriedText;
    }

    /// <summary>Opens a run of characters, writes the whitespace an element stands in for, or enters properties.</summary>
    private static void ReadRunElement(
        XmlReader reader,
        BoundedTextAccumulator text,
        ref bool insideRun,
        ref int propertiesDepth)
    {
        switch (reader.LocalName)
        {
            case "pPr" when !reader.IsEmptyElement:
                propertiesDepth = reader.Depth;
                insideRun = false;
                break;

            case "t":
                insideRun = !reader.IsEmptyElement;
                break;

            case "tab":
                text.Add(" ");
                insideRun = false;
                break;

            case "br" or "cr":
                text.EndLine();
                insideRun = false;
                break;

            default:
                insideRun = false;
                break;
        }
    }

    /// <summary>Reads the workbook's shared string table, which most cells hold their text in.</summary>
    private List<string> ReadSharedStrings(
        ZipArchive archive,
        DecompressionBudget budget,
        CancellationToken cancellationToken)
    {
        var strings = new List<string>();
        var entry = archive.GetEntry(SharedStringsPart);

        if (entry is null)
        {
            return strings;
        }

        // The table is held in memory, so it is bounded by the same ceiling the output is: a workbook whose strings
        // alone pass what one attachment may contribute could not have produced a smaller answer anyway. The entry
        // count is bounded against that number too, because an entry costs a list slot whether or not it carries a
        // character — a table of self-closed entries would otherwise inflate to the container budget while the
        // character ceiling never fired, and no entry past that many could contribute a character the ceiling admits.
        var table = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var item = new StringBuilder();
        var insideRun = false;

        using var reader = this.parts.OpenPart(entry, budget);

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == SpreadsheetNamespace:
                    if (reader.LocalName == "si")
                    {
                        item.Clear();

                        // A self-closed entry raises no end element, so recording it here is what keeps every later
                        // index in the table pointing at its own words rather than at the next entry's.
                        if (reader.IsEmptyElement)
                        {
                            AddBounded(strings, string.Empty, options.MaxExtractedTextCharacters);
                        }
                    }

                    insideRun = reader.LocalName == "t" && !reader.IsEmptyElement;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (insideRun)
                    {
                        table.Add(reader.Value);
                        item.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement when reader.NamespaceURI == SpreadsheetNamespace:
                    if (reader.LocalName == "t")
                    {
                        insideRun = false;
                    }
                    else if (reader.LocalName == "si")
                    {
                        AddBounded(strings, item.ToString(), options.MaxExtractedTextCharacters);
                    }

                    break;

                default:
                    break;
            }
        }

        return strings;
    }

    /// <summary>Collects the text every cell of one worksheet holds, one cell to a line.</summary>
    /// <returns><see langword="true" /> when a cell carried anything but whitespace; otherwise <see langword="false" />.</returns>
    private bool ReadCellsInto(
        XmlReader reader,
        List<string> sharedStrings,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var carriedText = false;

        while (this.parts.ReadNode(reader, cancellationToken))
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.NamespaceURI != SpreadsheetNamespace
                || reader.LocalName != "c"
                || reader.IsEmptyElement)
            {
                continue;
            }

            var cellType = reader.GetAttribute("t");

            using var cell = reader.ReadSubtree();

            var value = this.ReadCellText(cell, cellType, sharedStrings, cancellationToken);

            if (!string.IsNullOrEmpty(value))
            {
                text.Add(value);
                text.EndLine();
                carriedText |= !string.IsNullOrWhiteSpace(value);
            }
        }

        return carriedText;
    }

    /// <summary>Reads one cell, which holds its text in the string table, inline, or as a formula's result.</summary>
    /// <remarks>
    /// <para>
    /// A cell whose type is numeric, boolean, an error, or a date yields nothing. Extraction reads what somebody wrote
    /// rather than what a workbook computes: no formula is evaluated, and a number is not text a search should match on.
    /// </para>
    /// <para>
    /// One cell is gathered before any of it is handed on, because what a cell holds is decided by its type once the
    /// whole subtree has been read — so the output ceiling is applied here as well, against each of the two buffers.
    /// Without it a single cell would inflate to whatever the container budget allows before the accumulator ever saw
    /// it, and a cell whose type yields nothing would inflate without ever being measured at all.
    /// </para>
    /// </remarks>
    private string? ReadCellText(
        XmlReader cell,
        string? cellType,
        List<string> sharedStrings,
        CancellationToken cancellationToken)
    {
        var value = new StringBuilder();
        var inlineText = new StringBuilder();
        var insideValue = false;
        var insideInlineRun = false;

        while (this.parts.ReadNode(cell, cancellationToken))
        {
            switch (cell.NodeType)
            {
                case XmlNodeType.Element when cell.NamespaceURI == SpreadsheetNamespace:
                    insideValue = cell.LocalName == "v" && !cell.IsEmptyElement;
                    insideInlineRun = cell.LocalName == "t" && !cell.IsEmptyElement;
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (insideValue)
                    {
                        this.AppendBounded(value, cell.Value);
                    }
                    else if (insideInlineRun)
                    {
                        this.AppendBounded(inlineText, cell.Value);
                    }

                    break;

                case XmlNodeType.EndElement when cell.NamespaceURI == SpreadsheetNamespace:
                    insideValue &= cell.LocalName != "v";
                    insideInlineRun &= cell.LocalName != "t";
                    break;

                default:
                    break;
            }
        }

        return cellType switch
        {
            "s" => ResolveSharedString(value.ToString(), sharedStrings),
            "inlineStr" => inlineText.ToString(),
            "str" => value.ToString(),
            _ => null,
        };
    }

    /// <summary>Adds a run of a cell's characters, refusing the cell that grows past the output ceiling.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the addition would pass the ceiling.</exception>
    private void AppendBounded(StringBuilder target, string value)
    {
        if (target.Length + value.Length > options.MaxExtractedTextCharacters)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ExtractedTextTooLarge);
        }

        target.Append(value);
    }

    /// <summary>Records one string-table entry, refusing the table that holds more than the output ceiling allows.</summary>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the table passes that many entries.</exception>
    private static void AddBounded(List<string> strings, string entry, int maxEntries)
    {
        if (strings.Count >= maxEntries)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        strings.Add(entry);
    }

    /// <summary>Resolves the string-table index a cell holds, or nothing when the index names no entry.</summary>
    private static string? ResolveSharedString(string index, List<string> sharedStrings) =>
        int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
        && position >= 0
        && position < sharedStrings.Count
            ? sharedStrings[position]
            : null;

    /// <summary>Selects the numbered parts one pattern names, in the order their numbers give.</summary>
    /// <remarks>
    /// A part name is written by whoever composed the archive, so the number in one is parsed rather than trusted: a
    /// name carrying more digits than an integer holds orders last instead of throwing.
    /// </remarks>
    private static List<ZipArchiveEntry> OrderedParts(ZipArchive archive, Regex pattern) =>
    [
        .. archive.Entries
            .Select(entry => (entry, match: pattern.Match(entry.FullName)))
            .Where(candidate => candidate.match.Success)
            .OrderBy(candidate => int.TryParse(
                candidate.match.Groups[1].ValueSpan,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var number)
                ? number
                : int.MaxValue)
            .Select(candidate => candidate.entry),
    ];
}

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
/// Only the parts that carry text are opened. A macro project, an embedded object, an OLE package, an image, and every
/// other part of the package are never read, never decoded, and never handed to anything — extraction reads structure
/// and text and evaluates nothing.
/// </para>
/// </remarks>
internal sealed partial class OpenXmlAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    private const string WordprocessingNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string DrawingNamespace = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private const string WordDocumentPart = "word/document.xml";
    private const string SharedStringsPart = "xl/sharedStrings.xml";

    /// <summary>Builds the settings every XML part in this adapter is read under.</summary>
    /// <returns>Settings that resolve no entity and fetch nothing.</returns>
    /// <remarks>
    /// The two properties are the whole of the external-entity answer and they are set explicitly rather than left to a
    /// framework default, because a default is a decision somebody else may revise. <c>Prohibit</c> refuses a document
    /// type declaration outright, which is where an entity would have to be declared, and a null resolver leaves
    /// nothing able to fetch a resource even if one were.
    /// </remarks>
    internal static XmlReaderSettings PartReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>Reads one Office Open XML attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="format">Which of the three Office Open XML formats it is.</param>
    /// <param name="cancellationToken">Cancels the read between parts and between elements.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionBoundException">Thrown when a configured ceiling is crossed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the octets are not a readable archive.</exception>
    /// <exception cref="XmlException">Thrown when a part is not readable XML.</exception>
    public ExtractedAttachmentText Read(
        Stream content,
        AttachmentDocumentFormat format,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        if (archive.Entries.Count > options.MaxContainerParts)
        {
            throw new AttachmentTextExtractionBoundException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        var budget = new DecompressionBudget(options.MaxDecompressedOctets);
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

    [GeneratedRegex(@"^xl/worksheets/sheet(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex WorksheetPartPattern();

    /// <summary>Advances a reader one node, refusing an element tree nested past the configured depth.</summary>
    private bool ReadNode(XmlReader reader, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!reader.Read())
        {
            return false;
        }

        if (reader.Depth > options.MaxElementDepth)
        {
            throw new AttachmentTextExtractionBoundException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }

        return true;
    }

    /// <summary>Opens one archive part under the container's shared inflation budget.</summary>
    private XmlReader OpenPart(ZipArchiveEntry entry, DecompressionBudget budget) =>
        XmlReader.Create(
            new BoundedInflationStream(entry.Open(), entry.CompressedLength, budget, options.MaxDecompressionRatio),
            PartReaderSettings());

    /// <summary>Reads a word-processing document, whose body is one page because the format records no pagination.</summary>
    private ExtractedAttachmentText ReadDocument(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var document = archive.GetEntry(WordDocumentPart)
            ?? throw new InvalidDataException("The package declares no word-processing document part.");

        bool carriedText;

        using (var reader = this.OpenPart(document, budget))
        {
            carriedText = this.ReadRunsInto(reader, WordprocessingNamespace, text, cancellationToken);
        }

        return new ExtractedAttachmentText(text.ToText(), PageCount: 1, carriedText ? [] : [1]);
    }

    /// <summary>Reads a presentation, one page per slide in slide order.</summary>
    private ExtractedAttachmentText ReadPresentation(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var slides = OrderedParts(archive, SlidePartPattern());
        var slidesWithoutText = new List<int>();

        foreach (var (slide, index) in slides.Select((slide, index) => (slide, index)))
        {
            bool carriedText;

            using (var reader = this.OpenPart(slide, budget))
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

    /// <summary>Reads a workbook, one page per worksheet, resolving the shared string table each cell indexes into.</summary>
    /// <remarks>
    /// The string table is resolved rather than emitted whole. Emitting it would produce every word in the workbook
    /// attached to no sheet, which reads as text and says nothing about where the text is — and a sheet holding only a
    /// picture would be indistinguishable from one whose words are in the table.
    /// </remarks>
    private ExtractedAttachmentText ReadWorkbook(
        ZipArchive archive,
        DecompressionBudget budget,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var sharedStrings = this.ReadSharedStrings(archive, budget, cancellationToken);
        var sheets = OrderedParts(archive, WorksheetPartPattern());
        var sheetsWithoutText = new List<int>();

        foreach (var (sheet, index) in sheets.Select((sheet, index) => (sheet, index)))
        {
            bool carriedText;

            using (var reader = this.OpenPart(sheet, budget))
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
    /// Word-processing and presentation markup differ only in the namespace their text runs are written in, which is
    /// why one walk serves both: <c>t</c> holds the characters and <c>p</c> is what separates them into lines. The
    /// answer is what the part itself carried rather than how long the gathered text grew, because a line break written
    /// between two pages would otherwise read as the second page having said something.
    /// </remarks>
    private bool ReadRunsInto(
        XmlReader reader,
        string textNamespace,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var insideRun = false;
        var carriedText = false;

        while (this.ReadNode(reader, cancellationToken))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == textNamespace:
                    insideRun = reader.LocalName == "t" && !reader.IsEmptyElement;
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
        // alone pass what one attachment may contribute could not have produced a smaller answer anyway.
        var table = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var item = new StringBuilder();
        var insideRun = false;

        using var reader = this.OpenPart(entry, budget);

        while (this.ReadNode(reader, cancellationToken))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == SpreadsheetNamespace:
                    if (reader.LocalName == "si")
                    {
                        item.Clear();
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
                        strings.Add(item.ToString());
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

        while (this.ReadNode(reader, cancellationToken))
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
    /// A cell whose type is numeric, boolean, an error, or a date yields nothing. Extraction reads what somebody wrote
    /// rather than what a workbook computes: no formula is evaluated, and a number is not text a search should match on.
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

        while (this.ReadNode(cell, cancellationToken))
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
                        value.Append(cell.Value);
                    }
                    else if (insideInlineRun)
                    {
                        inlineText.Append(cell.Value);
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

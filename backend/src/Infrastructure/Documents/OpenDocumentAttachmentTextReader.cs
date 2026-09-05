// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.IO.Compression;
using System.Xml;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads the text of an OpenDocument file out of the archive it is packaged as.</summary>
/// <remarks>
/// <para>
/// An OpenDocument package is a zip archive like an Office Open XML one, and it is read the same way and for the same
/// reason — the archive is walked here so every part is inflated under a budget rather than by a document model that
/// would have expanded it before this code ever saw it.
/// </para>
/// <para>
/// Where the two formats differ is the shape inside. Office Open XML puts each page in a part of its own, so its reader
/// selects parts; OpenDocument puts a whole document in one <c>content.xml</c>, so this walks that single part and
/// segments it by the element the format uses to begin a page. Only that part is opened — the styles, the settings, the
/// embedded objects, the images, and any Basic macro library in the package are never read.
/// </para>
/// <para>
/// Every character an OpenDocument file shows sits inside a paragraph or a heading, whatever encloses it, so one walk
/// serves all three formats: what changes between them is only which element begins a page.
/// </para>
/// </remarks>
internal sealed class OpenDocumentAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    private const string TextNamespace = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string TableNamespace = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string DrawingNamespace = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    private const string ContentPart = "content.xml";

    /// <summary>Reads one OpenDocument attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="format">Which of the three OpenDocument formats it is.</param>
    /// <param name="cancellationToken">Cancels the read between elements.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionBoundException">Thrown when a configured ceiling is crossed.</exception>
    /// <exception cref="InvalidDataException">Thrown when the octets are not a readable archive, or carry no content.</exception>
    /// <exception cref="XmlException">Thrown when the content part is not readable XML.</exception>
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

        var entry = archive.GetEntry(ContentPart)
            ?? throw new InvalidDataException("The package declares no content part.");

        var budget = new DecompressionBudget(options.MaxDecompressedOctets);
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);

        using var reader = XmlReader.Create(
            new BoundedInflationStream(entry.Open(), entry.CompressedLength, budget, options.MaxDecompressionRatio),
            OpenXmlAttachmentTextReader.PartReaderSettings());

        return this.ReadContent(reader, PageElementOf(format), text, cancellationToken);
    }

    /// <summary>Names the element that begins a page, or nothing for a format that paginates nowhere.</summary>
    /// <remarks>
    /// A word-processing document counts as one page for the same reason its Office Open XML equivalent does: the
    /// format records no pagination, and producing one would mean laying the document out.
    /// </remarks>
    private static (string Namespace, string LocalName)? PageElementOf(AttachmentDocumentFormat format) => format switch
    {
        AttachmentDocumentFormat.OpenDocumentText => null,
        AttachmentDocumentFormat.OpenDocumentSpreadsheet => (TableNamespace, "table"),
        AttachmentDocumentFormat.OpenDocumentPresentation => (DrawingNamespace, "page"),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not an OpenDocument package."),
    };

    /// <summary>Walks the content part, gathering paragraphs and closing a page where the format begins a new one.</summary>
    /// <remarks>
    /// A page's emptiness is decided by what that page itself carried rather than by how long the gathered text grew,
    /// because a line ended between two pages would otherwise read as the second page having said something. The
    /// enclosing depth is remembered so that a table nested inside a spreadsheet cell is read as part of its own sheet
    /// rather than counted as a second one.
    /// </remarks>
    private ExtractedAttachmentText ReadContent(
        XmlReader reader,
        (string Namespace, string LocalName)? pageElement,
        BoundedTextAccumulator text,
        CancellationToken cancellationToken)
    {
        var pagesWithoutText = new List<int>();
        var pageCount = pageElement is null ? 1 : 0;
        var pageDepth = -1;
        var pageCarriedText = false;
        var paragraphDepth = -1;
        var documentCarriedText = false;

        while (this.ReadNode(reader, cancellationToken))
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when pageElement is { } page
                    && pageDepth < 0
                    && reader.NamespaceURI == page.Namespace
                    && reader.LocalName == page.LocalName:
                    pageCount++;
                    pageCarriedText = false;

                    if (reader.IsEmptyElement)
                    {
                        pagesWithoutText.Add(pageCount);
                    }
                    else
                    {
                        pageDepth = reader.Depth;
                    }

                    break;

                case XmlNodeType.Element when reader.NamespaceURI == TextNamespace:
                    this.ReadTextElement(reader, text, ref paragraphDepth);
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (paragraphDepth >= 0)
                    {
                        text.Add(reader.Value);

                        if (!string.IsNullOrWhiteSpace(reader.Value))
                        {
                            pageCarriedText = true;
                            documentCarriedText = true;
                        }
                    }

                    break;

                case XmlNodeType.EndElement when paragraphDepth >= 0 && reader.Depth == paragraphDepth:
                    paragraphDepth = -1;
                    text.EndLine();
                    break;

                case XmlNodeType.EndElement when pageDepth >= 0 && reader.Depth == pageDepth:
                    if (!pageCarriedText)
                    {
                        pagesWithoutText.Add(pageCount);
                    }

                    pageDepth = -1;
                    text.EndLine();
                    break;

                default:
                    break;
            }
        }

        return pageElement is null
            ? new ExtractedAttachmentText(text.ToText(), PageCount: 1, documentCarriedText ? [] : [1])
            : new ExtractedAttachmentText(text.ToText(), pageCount, pagesWithoutText);
    }

    /// <summary>Opens a paragraph, or writes the whitespace an element stands in for.</summary>
    /// <remarks>
    /// The format writes a run of spaces, a tab, and a line break as elements rather than as characters, so a reader
    /// that only gathered text nodes would run the words on either side of one together — which changes what the
    /// document says rather than only how it looks.
    /// </remarks>
    private void ReadTextElement(XmlReader reader, BoundedTextAccumulator text, ref int paragraphDepth)
    {
        switch (reader.LocalName)
        {
            case "p" or "h" when paragraphDepth < 0:
                if (reader.IsEmptyElement)
                {
                    text.EndLine();
                }
                else
                {
                    paragraphDepth = reader.Depth;
                }

                break;

            case "s" or "tab" when paragraphDepth >= 0:
                text.Add(" ");
                break;

            case "line-break" when paragraphDepth >= 0:
                text.EndLine();
                break;

            default:
                break;
        }
    }

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
}

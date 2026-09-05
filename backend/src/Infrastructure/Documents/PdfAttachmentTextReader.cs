// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads a PDF's text, one page at a time, and records which of its pages carry none.</summary>
/// <remarks>
/// <para>
/// The library reads structure and text and executes nothing. A PDF may declare JavaScript, an embedded file, an
/// action on opening, and a form that submits somewhere; none of them is evaluated, followed, or fetched, because
/// nothing here asks the document for anything but its pages' characters.
/// </para>
/// <para>
/// Pages are read one at a time rather than through the whole document at once, so the cancellation the extractor's
/// timeout raises is observed between them and the output ceiling stops the read where it is reached. A page whose
/// content is an image carries no characters, and is recorded as such rather than passed over — that is the exact page
/// a later optical-character-recognition pass would be given.
/// </para>
/// <para>
/// What bounds this path is narrower than what bounds the two package formats, and stating it is the point of this
/// paragraph. The inflation total, the per-part ratio, and the element depth all live in the archive readers and none
/// of them reaches here: the library inflates a page's content streams itself, with no ceiling this code can set, and
/// builds that page's whole text before the output ceiling below can refuse it. So a PDF is bounded by the input
/// ceiling on the octets that arrive and by a timeout observed between pages, and a page whose content stream inflates
/// enormously is bounded by neither. Issue #1684 is where that gap is tracked.
/// </para>
/// </remarks>
internal sealed class PdfAttachmentTextReader(AttachmentTextExtractionOptions options)
{
    /// <summary>Reads one PDF attachment.</summary>
    /// <param name="content">The attachment's octets, positioned at the start.</param>
    /// <param name="cancellationToken">Cancels the read between pages.</param>
    /// <returns>What the document yielded.</returns>
    /// <exception cref="AttachmentTextExtractionStoppedException">Thrown when the output ceiling is crossed.</exception>
    public ExtractedAttachmentText Read(Stream content, CancellationToken cancellationToken)
    {
        var text = new BoundedTextAccumulator(options.MaxExtractedTextCharacters);
        var pagesWithoutText = new List<int>();

        using var document = PdfDocument.Open(content, ReadOnlyParsingOptions());

        for (var page = 1; page <= document.NumberOfPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = ContentOrderTextExtractor.GetText(document.GetPage(page));

            if (string.IsNullOrWhiteSpace(pageText))
            {
                pagesWithoutText.Add(page);
                continue;
            }

            text.Add(pageText);
            text.EndLine();
        }

        return new ExtractedAttachmentText(text.ToText(), document.NumberOfPages, pagesWithoutText);
    }

    /// <summary>Builds the options every PDF here is opened under.</summary>
    /// <remarks>
    /// Both settings widen what is readable rather than what is trusted. Real mail carries PDFs written by generators
    /// that disagree with the specification in small ways, and a strict parse would report a contract somebody can open
    /// in their reader as unreadable; a font the document names and does not embed is a defect in the document's
    /// presentation rather than a reason to abandon its words. Neither relaxes a bound: what stops a hostile file is
    /// the input ceiling, the output ceiling, and the timeout, none of which this touches.
    /// </remarks>
    private static ParsingOptions ReadOnlyParsingOptions() => new()
    {
        UseLenientParsing = true,
        SkipMissingFonts = true,
    };
}

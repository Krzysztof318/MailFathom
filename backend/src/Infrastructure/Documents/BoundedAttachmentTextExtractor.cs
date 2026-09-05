// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction.Attachments;
using UglyToad.PdfPig.Exceptions;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads one attachment's text under every configured ceiling, and turns whatever a parser does into a reason.</summary>
/// <remarks>
/// <para>
/// This is the boundary the issue's whole posture rests on: below it are document parsers reading octets a hostile
/// sender composed, and above it are characters and a closed set of reasons. Nothing a parser raises crosses it, and
/// nothing it reads is written to the file system — no parser here needs a path, so the question of a location a later
/// step could execute never arises.
/// </para>
/// <para>
/// The timeout is observed between units of work rather than imposed on one: no parser here accepts a cancellation
/// token, and .NET cannot abort a thread, so a parser that never returns from one page or one part is bounded by what
/// its own path bounds instead. For a package format that is the inflation total, the per-part ratio, and the element
/// depth, none of which is optional for that reason. For a PDF it is the input ceiling alone, because the library
/// inflates a page's content streams itself with no ceiling this code can set — <see cref="PdfAttachmentTextReader" />
/// states that and issue #1684 is where it is tracked.
/// </para>
/// </remarks>
internal sealed class BoundedAttachmentTextExtractor(
    AttachmentTextExtractionOptions options,
    TimeProvider timeProvider) : IAttachmentTextExtractor
{
    /// <summary>The eight octets an OLE compound file opens with.</summary>
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly PdfAttachmentTextReader pdf = new(options);
    private readonly OpenXmlAttachmentTextReader openXml = new(options);
    private readonly OpenDocumentAttachmentTextReader openDocument = new(options);

    /// <inheritdoc />
    public async Task<AttachmentTextExtractionResult> ExtractTextAsync(
        IOpenedEmailAttachment attachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        var description = attachment.Description;

        if (AttachmentDocumentFormats.Recognize(description.MediaType, description.FileName) is not { } format)
        {
            return AttachmentTextExtractionResult.FormatNotRecognized();
        }

        if (!AttachmentDocumentFormats.IsExtracted(format) || !options.Formats.Contains(format))
        {
            return AttachmentTextExtractionResult.FormatNotExtracted();
        }

        if (description.DecodedSizeOctets > options.MaxInputOctets)
        {
            return AttachmentTextExtractionResult.InputTooLarge();
        }

        using var deadline = new CancellationTokenSource(options.Timeout, timeProvider);
        using var extraction = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        using var buffer = new BoundedAttachmentBuffer(options.MaxInputOctets);

        try
        {
            await attachment.WriteContentToAsync(buffer, extraction.Token);
        }
        catch (AttachmentTextExtractionStoppedException stopped)
        {
            return Stopped(stopped);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AttachmentTextExtractionResult.TimedOut();
        }

        var content = buffer.ToReadableStream();

        if (format is not AttachmentDocumentFormat.Pdf && IsCompoundFile(content))
        {
            return AttachmentTextExtractionResult.Encrypted();
        }

        try
        {
            return AttachmentTextExtractionResult.Extracted(this.ReadText(content, format, extraction.Token));
        }
        catch (AttachmentTextExtractionStoppedException stopped)
        {
            return Stopped(stopped);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AttachmentTextExtractionResult.TimedOut();
        }
        catch (PdfDocumentEncryptedException)
        {
            return AttachmentTextExtractionResult.Encrypted();
        }
        catch (Exception failure) when (IsParserFailure(failure))
        {
            return AttachmentTextExtractionResult.Malformed();
        }
    }

    /// <summary>Hands the buffered attachment to the parser its format names.</summary>
    private ExtractedAttachmentText ReadText(
        Stream content,
        AttachmentDocumentFormat format,
        CancellationToken cancellationToken) => format switch
        {
            AttachmentDocumentFormat.Pdf => this.pdf.Read(content, cancellationToken),
            AttachmentDocumentFormat.OpenDocumentText
                or AttachmentDocumentFormat.OpenDocumentSpreadsheet
                or AttachmentDocumentFormat.OpenDocumentPresentation =>
                this.openDocument.Read(content, format, cancellationToken),
            _ => this.openXml.Read(content, format, cancellationToken),
        };

    /// <summary>States whether octets a package format was expected in are an OLE compound file instead.</summary>
    /// <remarks>
    /// A password-protected <c>.docx</c>, <c>.xlsx</c>, <c>.pptx</c>, or OpenDocument file is not an archive at all:
    /// the package is encrypted whole and wrapped in an OLE compound file, which opens with the eight octets this
    /// reads. Without the check the archive reader refuses those octets and the answer is <c>Malformed</c>, which tells
    /// an owner their document is broken when what it is is locked — different facts with different remedies. Nothing
    /// else reaches here wearing that signature: a legacy binary format carries it too, and recognition has already
    /// answered those as a format nothing reads.
    /// </remarks>
    private static bool IsCompoundFile(Stream content)
    {
        if (content.Length < CompoundFileSignature.Length)
        {
            return false;
        }

        Span<byte> opening = stackalloc byte[CompoundFileSignature.Length];

        content.ReadExactly(opening);
        content.Position = 0;

        return opening.SequenceEqual(CompoundFileSignature);
    }

    /// <summary>Reports a read the adapter stopped as the outcome it carried up.</summary>
    private static AttachmentTextExtractionResult Stopped(AttachmentTextExtractionStoppedException stopped) =>
        stopped.Outcome switch
        {
            AttachmentTextExtractionOutcome.InputTooLarge => AttachmentTextExtractionResult.InputTooLarge(),
            AttachmentTextExtractionOutcome.ExtractedTextTooLarge => AttachmentTextExtractionResult.ExtractedTextTooLarge(),
            AttachmentTextExtractionOutcome.Encrypted => AttachmentTextExtractionResult.Encrypted(),
            _ => AttachmentTextExtractionResult.ContainerBoundExceeded(),
        };

    /// <summary>States whether a failure is a parser's reading of hostile octets rather than something this process owes a caller.</summary>
    /// <remarks>
    /// The catch is deliberately wide. A document parser handed adversarial input raises whatever its own reading of
    /// that input produces — a format exception, an argument exception from a length it computed, an index out of a
    /// range it derived, an arithmetic overflow — and enumerating those is a list that goes stale the first time a
    /// parser is updated. What must never be swallowed is enumerated instead, because that list is short and stable:
    /// cancellation belongs to whoever asked for it, and a process out of memory is not a fact about one attachment.
    /// </remarks>
    private static bool IsParserFailure(Exception failure) =>
        failure is not OperationCanceledException and not OutOfMemoryException;
}

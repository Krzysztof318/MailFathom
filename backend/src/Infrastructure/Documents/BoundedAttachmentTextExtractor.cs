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
/// nothing it reads is written to the file system — neither parser needs a path, so the question of a location a later
/// step could execute never arises.
/// </para>
/// <para>
/// The timeout is observed between units of work rather than imposed on one: neither parser accepts a cancellation
/// token, and .NET cannot abort a thread, so a parser that never returns from one page or one part is bounded by the
/// size, ratio, and depth ceilings instead. Those are not optional for that reason.
/// </para>
/// </remarks>
internal sealed class BoundedAttachmentTextExtractor(
    AttachmentTextExtractionOptions options,
    TimeProvider timeProvider) : IAttachmentTextExtractor
{
    private readonly PdfAttachmentTextReader pdf = new(options);
    private readonly OpenXmlAttachmentTextReader openXml = new(options);

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

        try
        {
            return AttachmentTextExtractionResult.Extracted(
                await this.ReadTextAsync(attachment, format, extraction.Token));
        }
        catch (AttachmentTextExtractionBoundException crossed)
        {
            return CrossedBound(crossed);
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

    /// <summary>Buffers the attachment under the input ceiling and hands it to the parser its format names.</summary>
    private async Task<ExtractedAttachmentText> ReadTextAsync(
        IOpenedEmailAttachment attachment,
        AttachmentDocumentFormat format,
        CancellationToken cancellationToken)
    {
        using var buffer = new BoundedAttachmentBuffer(options.MaxInputOctets);

        await attachment.WriteContentToAsync(buffer, cancellationToken);

        var content = buffer.ToReadableStream();

        return format is AttachmentDocumentFormat.Pdf
            ? this.pdf.Read(content, cancellationToken)
            : this.openXml.Read(content, format, cancellationToken);
    }

    /// <summary>Reports the bound a read crossed as the outcome that bound is published as.</summary>
    private static AttachmentTextExtractionResult CrossedBound(AttachmentTextExtractionBoundException crossed) =>
        crossed.Outcome switch
        {
            AttachmentTextExtractionOutcome.InputTooLarge => AttachmentTextExtractionResult.InputTooLarge(),
            AttachmentTextExtractionOutcome.ExtractedTextTooLarge => AttachmentTextExtractionResult.ExtractedTextTooLarge(),
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

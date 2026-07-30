// Copyright © 2026 Krzysztof Kasprowicz

using System.Text;
using System.Text.RegularExpressions;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;
using MimeKit;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Renders stored raw MIME for a reader, parsing with MimeKit and sanitizing with the pinned HTML sanitizer.</summary>
/// <remarks>
/// <para>
/// The parse mirrors extraction's: a forward-only structural pass abandons a message that declares more parts or
/// deeper nesting than the configured limits, and only a message that survives it is built into an object tree. Both
/// paths therefore refuse the same messages, so mail that could not be indexed cannot be displayed either — which is
/// what keeps a structurally hostile message from being answered differently depending on which door it came through.
/// </para>
/// <para>
/// Every reason a parse fails is reported as one unreadable outcome, because this caller acts identically on all of
/// them. Attachment content is never materialized here any more than it is during extraction: sizes are measured by
/// streaming, and no part's bytes leave this method.
/// </para>
/// <para>
/// What bounds the work is the size limit the message was stored under. A body is read in full and then cut to what is
/// returned, so the truncation metadata states the length that actually existed rather than the length of a read that
/// stopped early; markup is the exception and is cut before it is parsed, since sanitizing is the expensive step and
/// there is nothing to learn from parsing what will not be returned.
/// </para>
/// </remarks>
internal sealed class MimeKitEmailContentRenderer : IEmailContentRenderer
{
    private readonly EmailMimeExtractionOptions structuralLimits;
    private readonly EmailContentReadOptions readOptions;
    private readonly EmailHtmlSanitizer sanitizer = new();

    /// <summary>Initializes a renderer.</summary>
    /// <param name="structuralLimits">The limits a stored message's structure must stay within to be parsed at all.</param>
    /// <param name="readOptions">What one read may return.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public MimeKitEmailContentRenderer(
        EmailMimeExtractionOptions structuralLimits,
        EmailContentReadOptions readOptions)
    {
        ArgumentNullException.ThrowIfNull(structuralLimits);
        ArgumentNullException.ThrowIfNull(readOptions);

        this.structuralLimits = structuralLimits;
        this.readOptions = readOptions;
    }

    /// <inheritdoc />
    public async Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        bool includeSanitizedHtml,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var structuralPass = RawMimeStream.Open(content.RawMime);

        var exceededLimit = await MimeStructureLimitReader.FindExceededLimitAsync(
            structuralPass,
            this.structuralLimits,
            cancellationToken);

        if (exceededLimit != ExceededMimeStructureLimit.None)
        {
            return EmailContentRenderingResult.Unreadable();
        }

        await using var parsingPass = RawMimeStream.Open(content.RawMime);

        try
        {
            using var message = await MimeMessage.LoadAsync(
                ParserOptions.Default,
                parsingPass,
                persistent: true,
                cancellationToken);

            return EmailContentRenderingResult.Rendered(
                await this.RenderAsync(message, includeSanitizedHtml, cancellationToken));
        }
        catch (FormatException)
        {
            // Bytes that no longer parse are a damaged or badly formed local copy. Both are the caller's to act on,
            // and both leave nothing a reader could be shown.
            return EmailContentRenderingResult.Unreadable();
        }
        catch (RegexMatchTimeoutException)
        {
            // Content that defeats the bounded scan for embedded-resource references is reported the same way, so one
            // crafted message costs this request and nothing beyond it.
            return EmailContentRenderingResult.Unreadable();
        }
    }

    private async Task<EmailContentRendering> RenderAsync(
        MimeMessage message,
        bool includeSanitizedHtml,
        CancellationToken cancellationToken)
    {
        var classification = await MimeAttachmentClassifier.ClassifyAsync(message, cancellationToken);

        return new EmailContentRendering(
            MimeMessageHeaderReader.Read(message),
            this.ReadPlainTextBody(classification),
            includeSanitizedHtml ? this.ReadSanitizedHtmlBody(classification) : null,
            classification.BodyIsEncrypted,
            classification.Attachments);
    }

    /// <summary>Reads the body as words, preferring what the sender wrote to a reading of how it was displayed.</summary>
    /// <remarks>
    /// A genuine <c>text/plain</c> part wins over every HTML alternative, and HTML is read only when the message
    /// offered no plain-text one. Unlike the text the index covers, nothing is trimmed here: quoted history and a
    /// signature block are part of the message a person asked to read, and removing them by heuristic would hand back
    /// a message nobody sent.
    /// </remarks>
    private EmailBodyRepresentation ReadPlainTextBody(MimeContentClassification classification)
    {
        if (classification.BodyIsEncrypted)
        {
            return EmailBodyRepresentation.Empty;
        }

        var body = ReadPlainTextParts(classification.BodyTextParts)
            ?? DeriveTextFromHtmlParts(classification.BodyTextParts)
            ?? string.Empty;

        return EmailBodyRepresentation.Bounded(body, this.readOptions.MaxBodyCharacters);
    }

    /// <summary>Sanitizes the HTML the message displays, or reports that it displays none.</summary>
    /// <remarks>
    /// The markup is cut before it is parsed, so a body far beyond the bound costs the bound rather than its own size.
    /// Cutting markup leaves elements open, and the sanitizer's parse closes them as it re-serializes the document, so
    /// what comes back is well-formed rather than a fragment ending mid-element. That is why the truncation is measured
    /// against the source rather than against the result.
    /// </remarks>
    private EmailBodyRepresentation? ReadSanitizedHtmlBody(MimeContentClassification classification)
    {
        if (classification.BodyIsEncrypted)
        {
            return null;
        }

        var htmlParts = classification.BodyTextParts.Where(part => part.IsHtml).ToArray();
        if (htmlParts.Length == 0)
        {
            return null;
        }

        var source = string.Join('\n', htmlParts.Select(part => part.Text));
        var boundedSource = MailTextBounds.TruncateAtTextElementBoundary(source, this.readOptions.MaxBodyCharacters);

        return new EmailBodyRepresentation(
            this.sanitizer.Sanitize(boundedSource),
            source.Length,
            boundedSource.Length < source.Length);
    }

    /// <summary>Reads the plain-text body, joining the parts when a message resolved several as its body.</summary>
    private static string? ReadPlainTextParts(IReadOnlyList<TextPart> bodyTextParts) =>
        JoinNormalized(bodyTextParts.Where(part => part.IsPlain).Select(part => part.Text));

    /// <summary>Derives the words an HTML body displays, which is only reached when no plain-text alternative exists.</summary>
    private static string? DeriveTextFromHtmlParts(IReadOnlyList<TextPart> bodyTextParts) =>
        JoinNormalized(bodyTextParts
            .Where(part => part.IsHtml)
            .Select(part => HtmlBodyTextReader.ReadDisplayedText(part.Text, int.MaxValue)));

    /// <summary>Joins what several body parts carried into one normalized text, or reports that there were none.</summary>
    /// <remarks>
    /// The bound passed to the normalizer is the widest one, because the body is cut after it has been read rather than
    /// while it is being read: the length that existed is what the truncation metadata has to state, and a read that
    /// stopped at the bound could not state it. What bounds the work instead is the size limit the raw MIME was stored
    /// under, which no stored message is above.
    /// </remarks>
    private static string? JoinNormalized(IEnumerable<string> parts)
    {
        var texts = parts.ToArray();
        if (texts.Length == 0)
        {
            return null;
        }

        var body = new StringBuilder();
        foreach (var text in texts)
        {
            if (body.Length > 0)
            {
                MailBodyTextNormalizer.Append(body, "\n", int.MaxValue);
            }

            MailBodyTextNormalizer.Append(body, text, int.MaxValue);
        }

        return body.ToString().Trim();
    }
}

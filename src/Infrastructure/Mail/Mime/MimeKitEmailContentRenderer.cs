// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text;
using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

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
        var plainTextBody = this.ReadPlainTextBody(classification);

        // Encrypted content and an unreadable body are not the same claim. A multipart/alternative can offer a
        // readable member beside an encrypted one, and the classifier marks the branch encrypted because it holds
        // encrypted content somewhere; reporting that as "nothing can read this body" would discard text the message
        // itself provided for exactly this purpose. The state is therefore reserved for a body that is both encrypted
        // and left nothing readable behind.
        var bodyIsUnreadable = classification.BodyIsEncrypted && plainTextBody.Text.Length == 0;

        return new EmailContentRendering(
            MimeMessageHeaderReader.Read(message),
            bodyIsUnreadable ? EmailBodyRepresentation.Empty : plainTextBody,
            includeSanitizedHtml && !bodyIsUnreadable ? this.ReadSanitizedHtmlBody(classification) : null,
            bodyIsUnreadable,
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
        var body = ReadPlainTextParts(classification.BodyTextParts)
            ?? DeriveTextFromHtmlParts(classification.BodyTextParts)
            ?? string.Empty;

        return EmailBodyRepresentation.Bounded(body, this.readOptions.MaxBodyCharacters);
    }

    /// <summary>Sanitizes the HTML the message displays, or reports that it displays none.</summary>
    /// <remarks>
    /// <para>
    /// The markup is cut before it is parsed, so a body far beyond the bound costs the bound rather than its own size,
    /// and the sanitizer's parse closes what the cut left open as it re-serializes the document.
    /// </para>
    /// <para>
    /// Closing those elements adds characters, so a source that fits the bound can serialize past it — deeply nested
    /// markup can spend its whole allowance on opening tags and then need as much again to close them. Both properties
    /// a caller relies on are kept by shrinking the source instead of cutting the result: the returned markup stays
    /// balanced, and it stays within the bound the caller sized its handling against.
    /// </para>
    /// <para>
    /// The retry terminates because the serialized length never grows when the source shrinks — a shorter prefix opens
    /// no more elements than a longer one — and the budget strictly decreases on every pass. Ordinary mail never
    /// reaches a second pass.
    /// </para>
    /// </remarks>
    private EmailBodyRepresentation? ReadSanitizedHtmlBody(MimeContentClassification classification)
    {
        var htmlParts = classification.BodyTextParts.Where(part => part.IsHtml).ToArray();
        if (htmlParts.Length == 0)
        {
            return null;
        }

        var maxCharacters = this.readOptions.MaxBodyCharacters;
        var source = string.Join('\n', htmlParts.Select(part => part.Text));
        var sourceBudget = maxCharacters;
        string boundedSource;
        string sanitized;

        do
        {
            boundedSource = MailTextBounds.TruncateAtTextElementBoundary(source, sourceBudget);
            sanitized = this.sanitizer.Sanitize(boundedSource);

            // The next budget is scaled by how far the result overshot rather than reduced by the overshoot itself.
            // Closing tags are proportional to the markup that opened them, so subtracting the overshoot would
            // undershoot to nothing on exactly the markup this loop exists for; scaling lands near the answer and the
            // explicit decrement guarantees the budget still falls when rounding would have left it unchanged.
            sourceBudget = Math.Min(
                sourceBudget - 1,
                (int)((long)boundedSource.Length * maxCharacters / Math.Max(sanitized.Length, 1)));
        }
        while (sanitized.Length > maxCharacters && sourceBudget > 0);

        return new EmailBodyRepresentation(
            // Markup that is nothing but tags can exhaust the source budget without ever fitting, and the bound is what
            // a caller can actually rely on. Cutting the result is the last resort for that case alone.
            sanitized.Length > maxCharacters
                ? MailTextBounds.TruncateAtTextElementBoundary(sanitized, maxCharacters)
                : sanitized,
            source.Length,
            boundedSource.Length < source.Length);
    }

    /// <summary>Reads the plain-text body, joining the parts when a message resolved several as its body.</summary>
    /// <remarks>
    /// The edges are left exactly as the sender wrote them. A leading indent can be the first line of a code block and
    /// a trailing blank line can be the shape of a signature, and trimming either would both alter the message and make
    /// the reported original length describe something other than what was read.
    /// </remarks>
    private static string? ReadPlainTextParts(IReadOnlyList<TextPart> bodyTextParts) =>
        JoinNormalized(bodyTextParts.Where(part => part.IsPlain).Select(part => part.Text));

    /// <summary>Derives the words an HTML body displays, which is only reached when no plain-text alternative exists.</summary>
    /// <remarks>
    /// This one is trimmed where the plain-text reading is not, because its edge whitespace belongs to the derivation
    /// rather than to the message: a body opening with a block element emits a line break before its first word, and
    /// returning that would report layout the sender never wrote as content they did.
    /// </remarks>
    private static string? DeriveTextFromHtmlParts(IReadOnlyList<TextPart> bodyTextParts) =>
        JoinNormalized(bodyTextParts
            .Where(part => part.IsHtml)
            .Select(part => HtmlBodyTextReader.ReadDisplayedText(part.Text, int.MaxValue)))
            ?.Trim();

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

        return body.ToString();
    }
}

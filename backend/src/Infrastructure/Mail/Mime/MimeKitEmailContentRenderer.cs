// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime.Rendering;
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
/// them. No attachment octet is ever materialized: a part's size is measured by streaming its decode into a counter, and
/// what the decode produced is discarded as it arrives.
/// </para>
/// <para>
/// What bounds the work is the size limit the message was stored under. A body is read in full and then cut to what is
/// returned, so the truncation metadata states the length that actually existed rather than the length of a read that
/// stopped early; markup is the exception and is cut before it is parsed, since sanitizing is the expensive step and
/// there is nothing to learn from parsing what will not be returned.
/// </para>
/// <para>
/// How much may be returned is the caller's to state rather than this adapter's to hold, because one of the two bounds
/// is spent across the emails of a single read. The adapter applies whichever is smaller and reports which one it was.
/// </para>
/// </remarks>
internal sealed class MimeKitEmailContentRenderer : IEmailContentRenderer
{
    private readonly EmailMimeExtractionOptions structuralLimits;
    private readonly EmailHtmlSanitizer sanitizer = new();

    /// <summary>Initializes a renderer.</summary>
    /// <param name="structuralLimits">The limits a stored message's structure must stay within to be parsed at all.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="structuralLimits" /> is <see langword="null" />.</exception>
    public MimeKitEmailContentRenderer(EmailMimeExtractionOptions structuralLimits)
    {
        ArgumentNullException.ThrowIfNull(structuralLimits);

        this.structuralLimits = structuralLimits;
    }

    /// <inheritdoc />
    public async Task<EmailContentRenderingResult> RenderAsync(
        StoredEmailContent content,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(bounds);

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
                await this.RenderAsync(message, bounds, cancellationToken));
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

    /// <summary>Renders the parsed message within the bounds the read allows it.</summary>
    /// <remarks>
    /// The plain text is bounded first and what it returned is taken off the read's remaining budget before the markup
    /// is bounded, because the plain text is the representation every caller receives and the markup the one it opted
    /// into. A shared budget spent the other way round would starve the default representation for the sake of the
    /// extra one.
    /// </remarks>
    private async Task<EmailContentRendering> RenderAsync(
        MimeMessage message,
        EmailContentRenderingBounds bounds,
        CancellationToken cancellationToken)
    {
        var classification = await MimeAttachmentClassifier.ClassifyAsync(message, cancellationToken);

        var plainTextBody = ReadPlainTextBody(
            classification,
            EmailBodyCharacterAllowance.Of(
                bounds.MaxCharactersPerRepresentation,
                bounds.RemainingCharactersForRead));

        // Encrypted content and an unreadable body are not the same claim. A multipart/alternative can offer a
        // readable member beside an encrypted one, and the classifier marks the branch encrypted because it holds
        // encrypted content somewhere; reporting that as "nothing can read this body" would discard text the message
        // itself provided for exactly this purpose. The state is therefore reserved for a body that is both encrypted
        // and left nothing readable behind.
        //
        // What the message left behind is the source length, not the returned one. A read's character budget can empty
        // this representation for a reason that belongs to the call rather than to the message — the emails named
        // before it spent the budget — and judging emptiness after the bound would answer "this message can never be
        // read locally" to a message that reads fine when named on its own.
        var bodyIsUnreadable = classification.BodyIsEncrypted && plainTextBody.OriginalCharacterCount == 0;

        var sanitizedHtmlBody = bounds.IncludeSanitizedHtml && !bodyIsUnreadable
            ? this.ReadSanitizedHtmlBody(
                classification,
                EmailBodyCharacterAllowance.Of(
                    bounds.MaxCharactersPerRepresentation,
                    bounds.RemainingCharactersForRead - plainTextBody.Text.Length))
            : null;

        var htmlParts = classification.BodyTextParts.Where(part => part.IsHtml).Select(part => part.Text).ToArray();

        return new EmailContentRendering(
            MimeMessageHeaderReader.Read(message),
            bodyIsUnreadable ? EmailBodyRepresentation.Empty : plainTextBody,
            sanitizedHtmlBody,
            FormsOf(classification),
            bodyIsUnreadable,
            classification.Summary,
            classification.Attachments)
        {
            // The document is a representation of the same body and spends the same budget, so what the two before it
            // returned is subtracted from what it may reduce. A read naming several emails would otherwise return a
            // full document for each of them however much of the call's budget was already spent, which would make the
            // size of the answer the senders' decision rather than the deployment's.
            Document = bounds.IncludeMailDocument && !bodyIsUnreadable
                ? await MailBodyProjection.ProduceAsync(
                    message,
                    htmlParts,
                    bounds.RetainRemoteImageReferences,
                    EmailBodyCharacterAllowance.Of(
                        bounds.MaxCharactersPerRepresentation,
                        bounds.RemainingCharactersForRead
                            - plainTextBody.Text.Length
                            - (sanitizedHtmlBody?.Text.Length ?? 0)).MaxCharacters,
                    bounds.RemainingInlineImageOctetsForRead,
                    cancellationToken)
                : null,

            // The octets are the same budget the document draws on rather than a second one, and the two representations
            // are handed the same starting figure rather than one after the other. They are alternative renderings of the
            // same pictures — a reader sees the tree or the markup, never both at once — so spending the budget twice
            // would leave whichever was produced second drawing a message the first one had already emptied.
            SelfContainedHtmlBody = bounds.IncludeSelfContainedHtml && !bodyIsUnreadable
                ? await SelfContainedHtmlProjection.ProduceAsync(
                    message,
                    htmlParts,
                    bounds.RetainRemoteImageReferences,
                    EmailBodyCharacterAllowance.Of(
                        bounds.MaxCharactersPerRepresentation,
                        bounds.RemainingCharactersForRead
                            - plainTextBody.Text.Length
                            - (sanitizedHtmlBody?.Text.Length ?? 0)),
                    bounds.RemainingInlineImageOctetsForRead,
                    cancellationToken)
                : null,
        };
    }

    /// <summary>Names which forms of its own body the message wrote, out of the branch the walk settled on.</summary>
    /// <remarks>
    /// The body branch rather than the message's parts, which is the same source the two representations are produced
    /// from: a text file attached to a message is not a form of its body, and reporting it as one would tell a reader
    /// there are words to draw where there are none.
    /// </remarks>
    private static EmailBodyForms FormsOf(MimeContentClassification classification) => new(
        classification.BodyTextParts.Any(static part => !part.IsHtml),
        classification.BodyTextParts.Any(static part => part.IsHtml));

    /// <summary>Reads the body as words, preferring what the sender wrote to a reading of how it was displayed.</summary>
    /// <remarks>
    /// A genuine <c>text/plain</c> part wins over every HTML alternative, and HTML is read only when the message
    /// offered no plain-text one. Unlike the text the index covers, nothing is trimmed here: quoted history and a
    /// signature block are part of the message a person asked to read, and removing them by heuristic would hand back
    /// a message nobody sent.
    /// </remarks>
    private static EmailBodyRepresentation ReadPlainTextBody(
        MimeContentClassification classification,
        EmailBodyCharacterAllowance allowance)
    {
        var body = ReadPlainTextParts(classification.BodyTextParts)
            ?? DeriveTextFromHtmlParts(classification.BodyTextParts)
            ?? string.Empty;

        return EmailBodyRepresentation.Bounded(body, allowance);
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
    private EmailBodyRepresentation? ReadSanitizedHtmlBody(
        MimeContentClassification classification,
        EmailBodyCharacterAllowance allowance)
    {
        var htmlParts = classification.BodyTextParts.Where(part => part.IsHtml).ToArray();
        if (htmlParts.Length == 0)
        {
            return null;
        }

        var maxCharacters = allowance.MaxCharacters;
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
            boundedSource.Length < source.Length ? allowance.TruncationWhenCut : EmailBodyTruncation.None);
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

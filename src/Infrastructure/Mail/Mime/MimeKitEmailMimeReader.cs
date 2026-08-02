// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Domain.Emails;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Loads a parsed message from raw MIME.</summary>
/// <param name="rawMime">The raw MIME, positioned at its first byte.</param>
/// <param name="cancellationToken">Cancels the load.</param>
/// <returns>The parsed message.</returns>
/// <remarks>
/// The step exists as a delegate so a test can prove that an over-limit message is abandoned before an object tree is
/// built, which an assertion about the returned failure alone cannot establish.
/// </remarks>
internal delegate Task<MimeMessage> ParsedMimeMessageLoader(Stream rawMime, CancellationToken cancellationToken);

/// <summary>Reads normalized email metadata out of raw MIME with MimeKit.</summary>
/// <remarks>
/// Extraction runs in two passes over the same bytes. The first is a forward-only structural read that abandons a
/// message declaring more parts or deeper nesting than the configured limits; only a message that survives it is parsed
/// into an object tree. Attachment content is never materialized: the parse reads part content out of the raw MIME in
/// place instead of copying it, and each attachment's size is measured by decoding the part into a counter that
/// discards what it is given.
/// </remarks>
internal sealed class MimeKitEmailMimeReader : IEmailMimeReader
{
    private readonly EmailMimeExtractionOptions options;
    private readonly ParsedMimeMessageLoader loadMessage;

    /// <summary>Initializes a reader that parses with MimeKit.</summary>
    /// <param name="options">The configured structural limits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> is <see langword="null" />.</exception>
    public MimeKitEmailMimeReader(EmailMimeExtractionOptions options)
        : this(options, LoadWithoutCopyingContentAsync)
    {
    }

    /// <summary>Initializes a reader whose message load is supplied, so a test can observe whether it happens.</summary>
    /// <param name="options">The configured structural limits.</param>
    /// <param name="loadMessage">Turns raw MIME into a parsed message.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> or <paramref name="loadMessage" /> is <see langword="null" />.</exception>
    internal MimeKitEmailMimeReader(EmailMimeExtractionOptions options, ParsedMimeMessageLoader loadMessage)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadMessage);

        this.options = options;
        this.loadMessage = loadMessage;
    }

    /// <inheritdoc />
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var structuralPass = RawMimeStream.Open(content.RawMime);

        var exceededLimit = await MimeStructureLimitReader.FindExceededLimitAsync(
            structuralPass,
            this.options,
            cancellationToken);

        if (exceededLimit != ExceededMimeStructureLimit.None)
        {
            return exceededLimit == ExceededMimeStructureLimit.PartCount
                ? EmailMimeExtractionResult.PartCountLimitExceeded()
                : EmailMimeExtractionResult.NestingDepthLimitExceeded();
        }

        await using var parsingPass = RawMimeStream.Open(content.RawMime);

        try
        {
            using var message = await this.loadMessage(parsingPass, cancellationToken);

            return EmailMimeExtractionResult.Extracted(
                await this.ExtractMetadataAsync(content.OccurrenceId, message, cancellationToken));
        }
        catch (FormatException)
        {
            // Badly formed mail is expected rather than exceptional: the occurrence is recorded as unreadable and the
            // batch continues past it.
            return EmailMimeExtractionResult.MalformedContent();
        }
        catch (RegexMatchTimeoutException)
        {
            // Content that defeats the bounded scan for embedded-resource references is reported the same way, because
            // the alternative is worse than an imprecise label: an exception here would leave the occurrence unstored
            // and its folder checkpoint unmoved, so the same message would block that folder on every later run.
            return EmailMimeExtractionResult.MalformedContent();
        }
    }

    /// <summary>Parses against the raw MIME in place rather than into parser-owned copies of every part.</summary>
    /// <remarks>
    /// A non-persistent parse copies each part's content into buffers the parser owns, so a message already bounded by
    /// <c>MaxRawMimeBytes</c> would be held twice for as long as extraction runs, which is the allocation the bound
    /// exists to refuse. Persistent parsing leaves the content in the stream and reads it when a part is decoded. That
    /// is safe here and only here: the stream is seekable, nothing else reads it, and it is disposed after the message
    /// that reads through it.
    /// </remarks>
    private static Task<MimeMessage> LoadWithoutCopyingContentAsync(Stream rawMime, CancellationToken cancellationToken) =>
        MimeMessage.LoadAsync(ParserOptions.Default, rawMime, persistent: true, cancellationToken);

    private async Task<ExtractedEmailMetadata> ExtractMetadataAsync(
        EmailOccurrenceId occurrenceId,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        var classification = await MimeAttachmentClassifier.ClassifyAsync(message, cancellationToken);
        var headers = MimeMessageHeaderReader.Read(message);

        return new ExtractedEmailMetadata(
            occurrenceId,
            headers.Subject,
            headers.SentAt,
            headers.ReceivedAt,
            headers.Participants,
            headers.ThreadReferences,
            classification.Attachments,
            EmailBodyTextExtractor.Extract(classification, this.options.MaxExtractedTextCharacters));
    }
}

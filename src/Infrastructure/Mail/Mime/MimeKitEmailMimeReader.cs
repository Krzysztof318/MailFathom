// Copyright © 2026 Krzysztof Kasprowicz

using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Domain.Emails;
using MimeKit;
using MimeKit.Utils;

namespace MailMcp.Infrastructure.Mail.Mime;

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

        await using var structuralPass = OpenRawMime(content);

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

        await using var parsingPass = OpenRawMime(content);

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

    /// <summary>Reads the raw MIME without copying it, so neither pass duplicates the payload.</summary>
    private static MemoryStream OpenRawMime(RemoteEmailContent content) =>
        MemoryMarshal.TryGetArray(content.RawMime, out var segment) && segment.Array is { } buffer
            ? new MemoryStream(buffer, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.RawMime.ToArray(), writable: false);

    private async Task<ExtractedEmailMetadata> ExtractMetadataAsync(
        EmailOccurrenceId occurrenceId,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        var classification = await MimeAttachmentClassifier.ClassifyAsync(message, cancellationToken);

        return new ExtractedEmailMetadata(
            occurrenceId,
            NormalizeSubject(message.Subject),
            ReadHeaderDate(message, HeaderId.Date),
            ReadHeaderDate(message, HeaderId.Received),
            ReadParticipants(message),
            EmailThreadReferences.Create(message.MessageId, message.InReplyTo, message.References),
            classification.Attachments,
            EmailBodyTextExtractor.Extract(classification, this.options.MaxExtractedTextCharacters));
    }

    private static string? NormalizeSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // A subject reaches logs, MCP responses, and future indexes as one value, so the line breaks a folded or
        // deliberately crafted header can carry are removed rather than passed on.
        var singleLine = new string([.. subject.Where(character => !char.IsControl(character))]).Trim();

        return singleLine.Length == 0 ? null : singleLine;
    }

    /// <summary>Reads every address the message wrote, each under the header role it appeared in.</summary>
    /// <remarks>
    /// The result is wrapped rather than handed over as the array a collection expression builds, so a consumer of the
    /// record cannot cast the list back to an array and write through it.
    /// </remarks>
    private static ReadOnlyCollection<EmailParticipant> ReadParticipants(MimeMessage message) =>
        new List<EmailParticipant>(
        [
            .. CreateParticipants(EmailAddressRole.Sender, message.Sender is null ? [] : [message.Sender]),
            .. CreateParticipants(EmailAddressRole.From, message.From.Mailboxes),
            .. CreateParticipants(EmailAddressRole.ReplyTo, message.ReplyTo.Mailboxes),
            .. CreateParticipants(EmailAddressRole.To, message.To.Mailboxes),
            .. CreateParticipants(EmailAddressRole.Cc, message.Cc.Mailboxes),
            .. CreateParticipants(EmailAddressRole.Bcc, message.Bcc.Mailboxes),
        ]).AsReadOnly();

    /// <summary>Turns one header's mailboxes into participants, dropping the ones that do not parse as addresses.</summary>
    /// <remarks>
    /// Group syntax is flattened to its members, because a group name is a label the sender chose rather than a
    /// recipient anything can be filtered by.
    /// </remarks>
    private static IEnumerable<EmailParticipant> CreateParticipants(
        EmailAddressRole role,
        IEnumerable<MailboxAddress> mailboxes) =>
        mailboxes
            .Select(mailbox => EmailAddress.TryCreate(mailbox.Name, mailbox.Address, out var address)
                ? new EmailParticipant(role, address)
                : null)
            .OfType<EmailParticipant>();

    /// <summary>Reads one date-bearing header in UTC, or nothing when it is absent or unparseable.</summary>
    /// <remarks>
    /// The <c>Received</c> header is read from the topmost occurrence, which the last receiving hop wrote, and its date
    /// follows the final semicolon of the trace. A header the sender wrote unparseably yields no timestamp rather than
    /// a guessed one.
    /// </remarks>
    private static DateTimeOffset? ReadHeaderDate(MimeMessage message, HeaderId headerId)
    {
        var headerValue = message.Headers[headerId];
        if (headerValue is null)
        {
            return null;
        }

        var dateText = headerId == HeaderId.Received
            ? ReadTraceDate(headerValue)
            : headerValue;

        return dateText is not null && DateUtils.TryParse(dateText, out var date)
            ? date.ToUniversalTime()
            : null;
    }

    private static string? ReadTraceDate(string receivedHeaderValue)
    {
        var separatorIndex = receivedHeaderValue.LastIndexOf(';');

        return separatorIndex < 0 || separatorIndex == receivedHeaderValue.Length - 1
            ? null
            : receivedHeaderValue[(separatorIndex + 1)..];
    }
}

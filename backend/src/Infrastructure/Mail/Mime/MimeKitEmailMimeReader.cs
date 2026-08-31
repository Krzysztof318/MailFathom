// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Dkim;
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
    private readonly ITrustedAuthenticationAuthorityReader trustedAuthorities;
    private readonly ILocalSenderVerifier? localSenderVerifier;
    private readonly ParsedMimeMessageLoader loadMessage;

    /// <summary>Initializes a reader that parses with MimeKit.</summary>
    /// <param name="options">The configured structural limits.</param>
    /// <param name="trustedAuthorities">Resolves the server whose sender-authentication statements an account believes.</param>
    /// <param name="localSenderVerifier">
    /// Verifies a message's own DKIM signatures where no trusted server statement was found, or
    /// <see langword="null" /> where this deployment verifies nothing itself and makes no lookup for it.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options" /> or <paramref name="trustedAuthorities" /> is <see langword="null" />.</exception>
    public MimeKitEmailMimeReader(
        EmailMimeExtractionOptions options,
        ITrustedAuthenticationAuthorityReader trustedAuthorities,
        ILocalSenderVerifier? localSenderVerifier)
        : this(options, trustedAuthorities, localSenderVerifier, LoadWithoutCopyingContentAsync)
    {
    }

    /// <summary>Initializes a reader whose message load is supplied, so a test can observe whether it happens.</summary>
    /// <param name="options">The configured structural limits.</param>
    /// <param name="trustedAuthorities">Resolves the server whose sender-authentication statements an account believes.</param>
    /// <param name="localSenderVerifier">Verifies a message's own DKIM signatures, or <see langword="null" /> where nothing does.</param>
    /// <param name="loadMessage">Turns raw MIME into a parsed message.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument other than the verifier is <see langword="null" />.</exception>
    internal MimeKitEmailMimeReader(
        EmailMimeExtractionOptions options,
        ITrustedAuthenticationAuthorityReader trustedAuthorities,
        ILocalSenderVerifier? localSenderVerifier,
        ParsedMimeMessageLoader loadMessage)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(trustedAuthorities);
        ArgumentNullException.ThrowIfNull(loadMessage);

        this.options = options;
        this.trustedAuthorities = trustedAuthorities;
        this.localSenderVerifier = localSenderVerifier;
        this.loadMessage = loadMessage;
    }

    /// <inheritdoc />
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        RemoteEmailContent content,
        MailOwnerId owner,
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
        // The walk measures every attachment and retains nothing of any of them, which is the only reading there is:
        // nothing in this system publishes a part's octets, so extraction and a reader ask the same question here.
        var classification = await MimeAttachmentClassifier.ClassifyAsync(message, cancellationToken);

        var headers = MimeMessageHeaderReader.Read(message);

        return new ExtractedEmailMetadata(
            occurrenceId,
            headers.Subject,
            headers.SentAt,
            headers.ReceivedAt,
            headers.Participants,
            headers.ThreadReferences,
            classification.Summary,
            EmailBodyTextExtractor.Extract(classification, this.options.MaxExtractedTextCharacters),
            await this.ReadSenderAuthenticationAsync(occurrenceId, message, cancellationToken))
        {
            Automation = MailAutomationReading.Read(message),
        };
    }

    /// <summary>Reads what was established about who sent the message, from the server that said so or from the bytes.</summary>
    /// <remarks>
    /// <para>
    /// The trusted header is asked first and its answer is final. Local verification is a fallback rather than a
    /// supplement: it runs only where no header this account trusts was found at all, because a server that spoke about
    /// the message saw the connection this process did not, and two verdicts of different provenance sitting beside
    /// each other would make <em>which one is this</em> a question every reader has to ask.
    /// </para>
    /// <para>
    /// The displayed sender is taken from <c>From</c> alone and never from <c>Sender</c>, unlike the participant a
    /// timeline names. The two headers answer different questions: the timeline wants whoever the message is from for a
    /// reader, while alignment is defined against the domain a mail client shows, which is <c>From</c>'s. The first
    /// mailbox wins where the header carried several, because a message can display only one sender.
    /// </para>
    /// </remarks>
    private async Task<SenderAuthentication> ReadSenderAuthenticationAsync(
        EmailOccurrenceId occurrenceId,
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        var headers = AuthenticationResultsHeaderReader.Read(message);
        var authority = this.trustedAuthorities.GetTrustedAuthority(occurrenceId.AccountId);
        var displayedSenderAddress = message.From.Mailboxes.FirstOrDefault()?.Address;

        if (this.localSenderVerifier is { } verifier
            && !SenderAuthenticationReading.FindsTrustedStatement(headers, authority))
        {
            return await verifier.VerifyAsync(message, displayedSenderAddress, cancellationToken);
        }

        return SenderAuthenticationReading.Read(headers, authority, displayedSenderAddress);
    }
}

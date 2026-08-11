// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Opens one attachment of a stored message, parsing with MimeKit exactly as the renderer does.</summary>
/// <remarks>
/// <para>
/// The parse mirrors the renderer's: the same structural pass abandons a message declaring more parts or deeper nesting
/// than the configured limits, and only a message that survives it is built into an object tree. That is what makes the
/// two doors into one mailbox refuse the same messages, and it is also what makes a position mean the same thing on
/// both — the attachment list a read published and the part a link resolves come from one walk under one set of rules.
/// </para>
/// <para>
/// The part is measured before it is handed over, so the download can state its length before its first octet. Nothing
/// is buffered by either step: the measuring pass discards what it decodes, and the write decodes straight into the
/// destination the caller supplies.
/// </para>
/// <para>
/// It reaches no mail server, no network, and no file system. It is handed bytes that were already stored, so opening an
/// attachment can neither affect a remote <c>\Seen</c> flag nor fetch anything a message points at.
/// </para>
/// </remarks>
internal sealed class MimeKitEmailAttachmentContentReader : IEmailAttachmentContentReader
{
    private readonly EmailMimeExtractionOptions structuralLimits;

    /// <summary>Initializes a reader.</summary>
    /// <param name="structuralLimits">The limits a stored message's structure must stay within to be parsed at all.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="structuralLimits" /> is <see langword="null" />.</exception>
    public MimeKitEmailAttachmentContentReader(EmailMimeExtractionOptions structuralLimits)
    {
        ArgumentNullException.ThrowIfNull(structuralLimits);

        this.structuralLimits = structuralLimits;
    }

    /// <inheritdoc />
    public async Task<OpenedEmailAttachmentResult> OpenAsync(
        StoredEmailContent content,
        int attachmentPosition,
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
            return OpenedEmailAttachmentResult.Unreadable();
        }

        return await OpenParsedAsync(content, attachmentPosition, cancellationToken);
    }

    /// <summary>Parses the message and opens the part at one position, or disposes everything it built.</summary>
    /// <remarks>
    /// Ownership of the parse moves to the opened attachment only on the one path that succeeds. Every other path
    /// disposes both here, which is why the two locals are cleared before the successful return rather than after it:
    /// the cleanup runs regardless and has to be able to tell the two cases apart.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The opened attachment is the returned value and owns the parse; its caller disposes it, which is what the port's IAsyncDisposable contract states.")]
    private static async Task<OpenedEmailAttachmentResult> OpenParsedAsync(
        StoredEmailContent content,
        int attachmentPosition,
        CancellationToken cancellationToken)
    {
        var parsingPass = RawMimeStream.Open(content.RawMime);
        MimeMessage? message = null;

        try
        {
            message = await MimeMessage.LoadAsync(
                ParserOptions.Default,
                parsingPass,
                persistent: true,
                cancellationToken);

            var parts = MimeAttachmentClassifier.FindAttachmentParts(message);
            if (attachmentPosition < 0 || attachmentPosition >= parts.Count)
            {
                return OpenedEmailAttachmentResult.NoSuchAttachment();
            }

            var part = parts[attachmentPosition];
            var description = await MimeAttachmentClassifier.DescribeAttachmentAsync(part, cancellationToken);

            var opened = new OpenedMimeAttachment(message, parsingPass, part, description);
            message = null;
            parsingPass = null;

            return OpenedEmailAttachmentResult.Opened(opened);
        }
        catch (FormatException)
        {
            // Bytes that no longer parse are a damaged or badly formed local copy, which is the caller's to act on and
            // is the same finding the renderer reports for the same message.
            return OpenedEmailAttachmentResult.Unreadable();
        }
        finally
        {
            message?.Dispose();

            if (parsingPass is not null)
            {
                await parsingPass.DisposeAsync();
            }
        }
    }
}

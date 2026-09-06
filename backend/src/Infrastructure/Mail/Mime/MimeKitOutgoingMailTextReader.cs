// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Mail.Delivery.Screening;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads the subject, the body representations, and every attachment's text back out of a message this deployment composed.</summary>
/// <remarks>
/// <para>
/// The markup is returned as it stands rather than sanitized, which is the whole difference between this reader and
/// the one that renders stored mail for a person. A sanitizer answers what a browser may be allowed to render; a
/// screen asks what will be transmitted, and an attribute, a comment, or a style block a sanitizer would strip leaves
/// in the message exactly like the text beside it.
/// </para>
/// <para>
/// No structural limit is applied to the message itself, for the same reason no parse failure is modelled: the bytes
/// were composed by this deployment's own composer moments earlier, against bounds the composition already enforced, so
/// a message that declares a thousand parts here is a defect in that composer rather than hostile input to survive.
/// What a parse failure produces is the parser's own exception, travelling as the defect it is.
/// </para>
/// <para>
/// <b>An attachment is the exception to that, and is read as hostile input.</b> Its octets are whatever a caller handed
/// the composition, so nothing about them was produced here — which is why they are offered to
/// <see cref="IAttachmentTextExtractor" /> rather than to a parser of this reader's own, and why the ceilings that port
/// declares are applied a second time across the message: the greatest octets one attachment may hold bound every
/// document in the message together, the greatest characters one may yield bound their texts together, and the time one
/// may take bounds the loop. Without the second reading a message of many small documents would cost each ceiling once
/// per attachment.
/// </para>
/// <para>
/// Nothing beyond that is materialized. <see cref="MimeMessage.TextBody" /> and <see cref="MimeMessage.HtmlBody" />
/// select one part apiece and decode that one, and an attachment nothing recognizes as a document is stepped over
/// without its octets reaching a parser at all.
/// </para>
/// </remarks>
/// <param name="attachmentText">Reads one attachment's text under the ceilings it declares, or says why it read none.</param>
/// <param name="bounds">The ceilings that reading is held to, applied here across the message.</param>
/// <param name="timeProvider">Measures what reading the message's attachments has spent so far.</param>
internal sealed class MimeKitOutgoingMailTextReader(
    IAttachmentTextExtractor attachmentText,
    AttachmentTextExtractionOptions bounds,
    TimeProvider timeProvider) : IOutgoingMailTextReader
{
    /// <inheritdoc />
    public async Task<OutgoingMailText> ReadAsync(
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing message is screened from the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        await using var parsingPass = RawMimeStream.Open(rawMime);

        using var message = await MimeMessage.LoadAsync(
            ParserOptions.Default,
            parsingPass,
            persistent: true,
            cancellationToken);

        var attachments = await this.ReadAttachmentsAsync(message, cancellationToken);

        // Each of the three is empty rather than absent where the message carries none, except the markup, which stays
        // absent: a message with no HTML alternative and a message whose HTML alternative is empty are the same thing
        // to a screen, and the composed value list drops both either way.
        return new OutgoingMailText(
            message.Subject ?? string.Empty,
            message.TextBody ?? string.Empty,
            message.HtmlBody)
        {
            AttachmentTexts = attachments.Texts,
            UnreadableAttachment = attachments.Unreadable,
        };
    }

    /// <summary>Reads every document the message attaches, and stops at the first one nothing could read.</summary>
    /// <remarks>
    /// It stops rather than reading on, because one unreadable document already refuses the act: everything after it
    /// would be work spent on a send that is not happening, and on octets a caller supplied.
    /// </remarks>
    private async Task<ReadAttachments> ReadAttachmentsAsync(
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        var parts = MimeAttachmentClassifier.FindAttachmentParts(message);
        if (parts.Count == 0)
        {
            return new ReadAttachments([], Unreadable: null);
        }

        var startedAt = timeProvider.GetTimestamp();
        var octets = 0L;
        var characters = 0L;
        var texts = new List<string>(parts.Count);

        foreach (var part in parts)
        {
            // Measured before the read rather than around it, because no reading here is interruptible: what this
            // bounds is how many more attachments are opened, so the message costs this ceiling plus the one attachment
            // that was already under way.
            if (timeProvider.GetElapsedTime(startedAt) > bounds.Timeout)
            {
                return new ReadAttachments([], AttachmentTextExtractionOutcome.TimedOut);
            }

            var description = await MimeAttachmentClassifier.DescribeAttachmentAsync(part, cancellationToken);

            await using var opened = new BorrowedMimeAttachment(part, description);

            var extracted = await attachmentText.ExtractTextAsync(opened, cancellationToken);

            // An attachment nothing recognized as a document is stepped over before it is counted against anything. It
            // reached no parser — the extractor answers this from the declaration alone — so a photograph must not be
            // able to spend the octets the documents beside it would have been read within.
            if (extracted.Outcome is AttachmentTextExtractionOutcome.FormatNotRecognized)
            {
                continue;
            }

            // Counted after the read rather than before it, because the read is already bounded on its own: the
            // extractor refuses an attachment declaring more than this ceiling before it buffers a byte of it. So what
            // this total bounds is how many more documents are opened, and the message costs the ceiling plus the one
            // attachment already read within it — never the sum of what a caller attached.
            octets += description.DecodedSizeOctets;
            if (octets > bounds.MaxInputOctets)
            {
                return new ReadAttachments([], AttachmentTextExtractionOutcome.InputTooLarge);
            }

            if (extracted.Text is not { } read)
            {
                return new ReadAttachments([], extracted.Outcome);
            }

            characters += read.Text.Length;
            if (characters > bounds.MaxExtractedTextCharacters)
            {
                return new ReadAttachments([], AttachmentTextExtractionOutcome.ExtractedTextTooLarge);
            }

            texts.Add(read.Text);
        }

        return new ReadAttachments(texts, Unreadable: null);
    }

    /// <summary>What reading a message's attachments produced: their texts, or the reason one of them yielded none.</summary>
    /// <remarks>
    /// The texts are empty whenever a reason is present, because a message with one unreadable document is refused
    /// whole — carrying the texts read before it would invite a caller to screen part of a message the screen has
    /// already decided it cannot judge.
    /// </remarks>
    private sealed record ReadAttachments(
        IReadOnlyList<string> Texts,
        AttachmentTextExtractionOutcome? Unreadable);

    /// <summary>One part of a message somebody else parsed, offered to the extractor for the length of one read.</summary>
    /// <remarks>
    /// It owns nothing and disposes nothing: the parse belongs to the read that opened it and outlives every attachment
    /// in the message, which is the difference between this and <see cref="OpenedMimeAttachment" />. Disposing the
    /// message here would end the walk partway through it.
    /// </remarks>
    private sealed class BorrowedMimeAttachment(MimeEntity part, ExtractedEmailAttachment description)
        : IOpenedEmailAttachment
    {
        /// <inheritdoc />
        public ExtractedEmailAttachment Description { get; } = description;

        /// <inheritdoc />
        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return MimeAttachmentClassifier.DecodeToAsync(part, destination, cancellationToken);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

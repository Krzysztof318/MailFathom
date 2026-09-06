// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.AttachmentText;
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
/// <see cref="IAttachmentTextExtractor" /> rather than to a parser of this reader's own. What bounds one attachment is
/// what that port declares; what bounds the message is <see cref="EmailAttachmentTextBounds" />, the same two numbers
/// the account run's own attachment stage is held to — how many of a message's <i>documents</i> are read, and the
/// octets they may be read from together. Reusing them rather than declaring a pair here is what keeps one message from
/// costing a different amount depending on which path met it, and it is why two ordinary large documents are read
/// rather than refused.
/// </para>
/// <para>
/// Both are counted over what the extractor recognized rather than over what the message declares, which is the one
/// place the two paths answer differently and deliberately so. The account run reads the documents that fit and leaves
/// the rest unindexed; a screen has no such option, so passing the count refuses the act. Counting parts instead would
/// refuse a message carrying six photographs on a deployment whose ceiling is five, and tell its author to send fewer
/// attached documents than a message holding none.
/// </para>
/// <para>
/// <b>One ceiling is this reader's own, and it exists because of who waits.</b> The extraction timeout bounds one
/// attachment, so a message carrying the greatest number of them would bound at that timeout multiplied by the count —
/// which a background stage can afford and a caller holding a send open cannot. So the same timeout is read a second
/// time across the whole walk: a screened send costs at most that budget plus the one attachment already under way.
/// <see cref="EmailAttachmentTextBounds.IsEnabled" /> is deliberately not read at all, because what a screen judges is
/// what would leave rather than what is worth indexing, and a deployment that derives no attachment text still screens
/// the documents it sends.
/// </para>
/// <para>
/// Nothing beyond that is materialized. <see cref="MimeMessage.TextBody" /> and <see cref="MimeMessage.HtmlBody" />
/// select one part apiece and decode that one, and an attachment nothing recognizes as a document is stepped over
/// without its octets reaching a parser at all.
/// </para>
/// </remarks>
/// <param name="attachmentText">Reads one attachment's text under the ceilings it declares, or says why it read none.</param>
/// <param name="perAttachment">The ceilings one attachment is read within, whose timeout is read again across the walk.</param>
/// <param name="perMessage">The ceilings a whole message's attachments are read within.</param>
/// <param name="timeProvider">Measures what reading the message's attachments has spent so far.</param>
internal sealed class MimeKitOutgoingMailTextReader(
    IAttachmentTextExtractor attachmentText,
    AttachmentTextExtractionOptions perAttachment,
    EmailAttachmentTextBounds perMessage,
    TimeProvider timeProvider) : IOutgoingMailTextReader
{
    /// <inheritdoc />
    public Task<OutgoingMailText> ReadWordsAsync(
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        this.ReadAsync(rawMime, readAttachments: false, cancellationToken);

    /// <inheritdoc />
    public Task<OutgoingMailText> ReadForScreeningAsync(
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        this.ReadAsync(rawMime, readAttachments: true, cancellationToken);

    /// <summary>Parses the message once, and opens its attachments only where the caller will judge them.</summary>
    private async Task<OutgoingMailText> ReadAsync(
        ReadOnlyMemory<byte> rawMime,
        bool readAttachments,
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

        var attachments = readAttachments
            ? await this.ReadAttachmentsAsync(message, cancellationToken)
            : new ReadAttachments([], Refusal: null);

        // Each of the three is empty rather than absent where the message carries none, except the markup, which stays
        // absent: a message with no HTML alternative and a message whose HTML alternative is empty are the same thing
        // to a screen, and the composed value list drops both either way.
        return new OutgoingMailText(
            message.Subject ?? string.Empty,
            message.TextBody ?? string.Empty,
            message.HtmlBody)
        {
            AttachmentTexts = attachments.Texts,
            AttachmentRefusal = attachments.Refusal,
        };
    }

    /// <summary>Reads every document the message attaches, and stops at the first thing that leaves the screen unable to judge it.</summary>
    /// <remarks>
    /// It stops rather than reading on, because either refusal already refuses the act: everything after it would be
    /// work spent on a send that is not happening, and on octets a caller supplied.
    /// </remarks>
    private async Task<ReadAttachments> ReadAttachmentsAsync(
        MimeMessage message,
        CancellationToken cancellationToken)
    {
        var parts = MimeAttachmentClassifier.FindAttachmentParts(message);
        if (parts.Count == 0)
        {
            return new ReadAttachments([], Refusal: null);
        }

        var startedAt = timeProvider.GetTimestamp();
        var octets = 0L;
        var documents = 0;
        var texts = new List<string>(parts.Count);

        foreach (var part in parts)
        {
            // Measured before the read rather than around it, because no reading here is interruptible: what this
            // bounds is how many more attachments are opened, so the message costs this ceiling plus the one attachment
            // that was already under way.
            if (timeProvider.GetElapsedTime(startedAt) > perAttachment.Timeout)
            {
                return new ReadAttachments([], OutgoingAttachmentRefusal.MessageCeilingReached);
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

            // Both totals are counted after the read rather than before it, because the read is already bounded on
            // its own: the extractor refuses an attachment declaring more than its own ceiling before it buffers a
            // byte of it, and it answers an unrecognized format from the declaration alone. So each bounds how many
            // more documents are opened, and the message costs the ceiling plus the one attachment already read
            // within it — never the sum of what a caller attached. Counting here rather than over the part list is
            // what keeps the two ceilings honest about the same thing: six photographs are six parts and no
            // documents, so a message carrying them is screened rather than refused for a count it never spent.
            documents++;
            octets += description.DecodedSizeOctets;
            if (documents > perMessage.MaxAttachmentsPerEmail || octets > perMessage.MaxInputOctetsPerEmail)
            {
                return new ReadAttachments([], OutgoingAttachmentRefusal.MessageCeilingReached);
            }

            if (extracted.Text is not { } read)
            {
                return new ReadAttachments([], OutgoingAttachmentRefusal.NotRead);
            }

            texts.Add(read.Text);
        }

        return new ReadAttachments(texts, Refusal: null);
    }

    /// <summary>What reading a message's attachments produced: their texts, or why there is nothing to judge them by.</summary>
    /// <remarks>
    /// The texts are empty whenever a refusal is present, because a message the screen cannot judge whole is refused
    /// whole — carrying the texts read before it would invite a caller to screen part of a message the screen has
    /// already decided it cannot judge. Which of the two refusals it is is decided here and nowhere else: this is the
    /// only place that knows whether the extractor refused one file or the message ran out of what a whole message may
    /// spend, and an author told the wrong one is told to convert a document that was read successfully.
    /// </remarks>
    private sealed record ReadAttachments(
        IReadOnlyList<string> Texts,
        OutgoingAttachmentRefusal? Refusal);

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

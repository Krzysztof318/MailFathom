// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Reads every attachment of one stored message and says, for each, what it yielded or why it yielded nothing.</summary>
/// <remarks>
/// <para>
/// The whole of the derivation happens here and none of it touches the database, which is the ordering the rest of this
/// path depends on: parsing a sender's document and describing a picture are the two slowest and least trustworthy
/// things this system does, and neither may run inside an open transaction. What the caller receives is a list of
/// values it can commit in one statement.
/// </para>
/// <para>
/// <b>Which of the two ports reads an attachment is decided by the extractor and not here.</b> It answers
/// <see cref="AttachmentTextExtractionOutcome.FormatNotRecognized" /> for anything whose media type and file name name
/// no document format, before it buffers an octet — so a picture reaches the describer by falling through that answer
/// rather than by a second copy of the format table kept in this file. Everything else the extractor answers is final,
/// including a document it declines to read: a spreadsheet nothing parses is not then offered to a vision model.
/// </para>
/// <para>
/// <b>Nothing reaches a mail server.</b> The octets are the stored raw MIME, read through the content store, and the
/// attachment is opened by the same walk position the download route is addressed with — so a citation into an
/// attachment resolves to the file a reader would actually open.
/// </para>
/// <para>
/// <b>Everything it produces is redacted before it is returned.</b> Attachment text is derived mail content exactly as
/// a body is, so it passes the same <see cref="SensitiveContentDerivationGuard" /> every other derived write goes
/// through, and inherits the stamp with it. A description is redacted too: a model asked what a picture shows will
/// read out the account number printed on it.
/// </para>
/// </remarks>
public sealed class EmailAttachmentTextDeriver
{
    private readonly IEmailContentStore contentStore;
    private readonly IEmailAttachmentContentReader attachmentReader;
    private readonly IAttachmentTextExtractor extractor;
    private readonly IEmailAttachmentImageDescriber describer;
    private readonly SensitiveContentDerivationGuard guard;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly AttachmentTextExtractionOptions extractionOptions;
    private readonly EmailAttachmentTextBounds bounds;

    /// <summary>Initializes the derivation from the stores and ports one message's attachments are read through.</summary>
    /// <param name="contentStore">Reads the stored raw MIME the attachments are opened from.</param>
    /// <param name="attachmentReader">Opens one attachment at a walk position.</param>
    /// <param name="extractor">Reads a document attachment's text, or says why it read none.</param>
    /// <param name="describer">Describes an image attachment, or says why it stayed a picture.</param>
    /// <param name="guard">Redacts what is about to be stored, and stamps what it was redacted under.</param>
    /// <param name="repairRequestStore">Records durably that a message's stored copy is missing or will not parse.</param>
    /// <param name="extractionOptions">The per-attachment ceilings, whose input bound also bounds what is buffered for a description.</param>
    /// <param name="bounds">The per-message and per-run ceilings.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailAttachmentTextDeriver(
        IEmailContentStore contentStore,
        IEmailAttachmentContentReader attachmentReader,
        IAttachmentTextExtractor extractor,
        IEmailAttachmentImageDescriber describer,
        SensitiveContentDerivationGuard guard,
        IEmailContentRepairRequestStore repairRequestStore,
        AttachmentTextExtractionOptions extractionOptions,
        EmailAttachmentTextBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(attachmentReader);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(extractionOptions);
        ArgumentNullException.ThrowIfNull(bounds);

        this.contentStore = contentStore;
        this.attachmentReader = attachmentReader;
        this.extractor = extractor;
        this.describer = describer;
        this.guard = guard;
        this.repairRequestStore = repairRequestStore;
        this.extractionOptions = extractionOptions;
        this.bounds = bounds;
    }

    /// <summary>Reads the attachments of one message.</summary>
    /// <param name="email">The message whose attachments are read, and the owner whose posture redacts them.</param>
    /// <param name="runBudget">What the account run has left to read, which this decrements as it reads.</param>
    /// <param name="cancellationToken">Cancels the read between attachments and inside one.</param>
    /// <returns>What each attachment yielded, in walk order, or nothing at all where the run budget stopped the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="SensitiveContent.Detection.SensitiveContentScannerUnavailableException">Thrown when a switched-on scanner could not establish what the text carries, which refuses the derivation.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// <para>
    /// An absent answer and an empty one are different facts and the caller acts on them differently. Nothing means the
    /// run had no budget left to reach this message, so it is left exactly as it was and the next run — which starts
    /// with a full budget — reads it. An empty list means the message <em>was</em> decided and yielded nothing, so it is
    /// recorded as read and never offered to a parser again.
    /// </para>
    /// <para>
    /// The walk reaches at most the message's count ceiling. A message declaring more parts than that has the rest
    /// neither opened nor recorded, which is the one ceiling that leaves no row behind: writing one per declared part
    /// would put the number of rows a message costs in the sender's hands, and the count stored against the message
    /// already says how many the ceiling left unread.
    /// </para>
    /// <para>
    /// A message whose raw MIME is not stored, and one whose stored MIME the walk cannot open, are the two the repair
    /// path exists for, so this pass does what every other reader of stored content does with them: it records an
    /// <see cref="EmailContentRepairRequest" /> and leaves the message unsettled, which keeps it out of the stamp and
    /// in the walk. Re-reading it costs one content-store read that answers with nothing until synchronization has
    /// fetched the message again, and the request is idempotent per message, so neither repeats into anything.
    /// </para>
    /// <para>
    /// A walk position the message turns out not to have is a different fact and is settled: the count on the row
    /// disagrees with the structure, which no repair changes and no repetition resolves.
    /// </para>
    /// </remarks>
    public async Task<EmailAttachmentTextDerivation?> DeriveAsync(
        EmailAwaitingAttachmentText email,
        EmailAttachmentTextRunBudget runBudget,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(runBudget);

        if (runBudget.IsExhausted)
        {
            return null;
        }

        // Read before the scan rather than at the write, exactly as the body's own redaction takes it: a posture
        // republished while this message is being read then leaves its rows stamped with the older configuration,
        // which reads as stale and is re-derived — the safe direction.
        var redactedUnder = this.guard.StampFor(email.Owner);

        var content = await this.contentStore.FindStoredContentAsync(email.Id, cancellationToken);

        if (content is null)
        {
            return await this.RequestRepairAsync(email.Id, EmailContentDefect.Missing, redactedUnder, cancellationToken);
        }

        var derived = new List<DerivedAttachmentText>();
        var readOctets = 0L;

        // The count ceiling bounds the walk itself rather than what the walk decides, because the number of parts a
        // message declares is the sender's. Opening each of them to write a refusal down would let a stranger choose
        // how many MIME parts this pass reads and how many rows it stores; what the ceiling stopped stays answerable
        // from the count on the message beside the readings actually stored.
        var walkLimit = Math.Min(email.AttachmentCount, this.bounds.MaxAttachmentsPerEmail);

        for (var position = 0; position < walkLimit; position++)
        {
            var opened = await this.attachmentReader.OpenAsync(content, position, cancellationToken);

            // Bytes that no longer parse are a damaged local copy rather than a fact about this attachment, so the
            // repair path is told and the message keeps its place in the walk; whatever earlier positions yielded is
            // still returned, and a re-read after the repair replaces it.
            if (opened.ContentIsUnreadable)
            {
                return await this.RequestRepairAsync(
                    email.Id,
                    EmailContentDefect.Unreadable,
                    redactedUnder,
                    cancellationToken,
                    derived);
            }

            // A position the message turns out not to have is the walk disagreeing with the count the row recorded.
            // Nothing repairs that and nothing about the file is worth writing down, so it ends the message.
            if (opened.Attachment is not { } attachment)
            {
                break;
            }

            await using (attachment)
            {
                var description = attachment.Description;
                var fileName = description.FileName?.Value;

                if (readOctets + description.DecodedSizeOctets > this.bounds.MaxInputOctetsPerEmail)
                {
                    derived.Add(DerivedAttachmentText.PastMessageBudget(position, description.MediaType, fileName));

                    continue;
                }

                if (!runBudget.TryReserve(description.DecodedSizeOctets))
                {
                    // The run rather than the message, so nothing about this message is written down: it is untouched
                    // and the next run reaches it with a full budget. A partial commit here would stamp the message as
                    // derived while most of it never was.
                    return null;
                }

                readOctets += description.DecodedSizeOctets;

                derived.Add(await this.ReadAsync(position, attachment, email.Owner, cancellationToken));
            }
        }

        return new EmailAttachmentTextDerivation(derived, redactedUnder);
    }

    /// <summary>Leaves a durable note that the stored copy needs fetching again, and hands back what was read so far.</summary>
    /// <remarks>
    /// The request is recorded outside any transaction, exactly as the download route records one: the note is worth
    /// keeping whether or not the commit that follows it succeeds.
    /// </remarks>
    private async Task<EmailAttachmentTextDerivation> RequestRepairAsync(
        StoredEmailId emailId,
        EmailContentDefect defect,
        SensitiveContentDerivationStamp? redactedUnder,
        CancellationToken cancellationToken,
        IReadOnlyList<DerivedAttachmentText>? read = null)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(emailId, defect),
            cancellationToken);

        return new EmailAttachmentTextDerivation(read ?? [], redactedUnder, AwaitsRepair: true);
    }

    /// <summary>Reads one opened attachment as a document, falling through to a description where it is not one.</summary>
    private async Task<DerivedAttachmentText> ReadAsync(
        int position,
        IOpenedEmailAttachment attachment,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var description = attachment.Description;
        var mediaType = description.MediaType;
        var fileName = description.FileName?.Value;

        var extracted = await this.extractor.ExtractTextAsync(attachment, cancellationToken);

        if (extracted.Outcome is not AttachmentTextExtractionOutcome.FormatNotRecognized)
        {
            return await this.RedactAsync(
                DerivedAttachmentText.FromExtraction(position, mediaType, fileName, extracted),
                owner,
                cancellationToken);
        }

        return await this.RedactAsync(
            DerivedAttachmentText.FromDescription(
                position,
                mediaType,
                fileName,
                await this.DescribeAsync(attachment, mediaType, cancellationToken)),
            owner,
            cancellationToken);
    }

    /// <summary>Buffers one attachment's octets and asks what the picture shows.</summary>
    /// <remarks>
    /// The describer reads a stream forward while <see cref="IOpenedEmailAttachment" /> writes to a destination, so the
    /// octets are held for the length of one call. The bound is the same per-attachment input ceiling a document is
    /// buffered under: what an attachment may cost to read is one number whichever port ends up reading it, and the
    /// message and run budgets above have already been charged for exactly these octets.
    /// </remarks>
    private async Task<ImageAttachmentDescription> DescribeAsync(
        IOpenedEmailAttachment attachment,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (attachment.Description.DecodedSizeOctets > this.extractionOptions.MaxInputOctets)
        {
            return ImageAttachmentDescription.Refused(ImageDescriptionRefusal.ImageTooLarge);
        }

        using var buffer = new MemoryStream();

        await attachment.WriteContentToAsync(buffer, cancellationToken);

        buffer.Position = 0;

        return await this.describer.DescribeAsync(mediaType, buffer, cancellationToken);
    }

    /// <summary>Replaces what the owner's switched-on scanner finds before the words leave this method.</summary>
    private async Task<DerivedAttachmentText> RedactAsync(
        DerivedAttachmentText derived,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        if (derived.Text is not { } text)
        {
            return derived;
        }

        return derived.WithRedactedText(await this.guard.GuardAsync(owner, text, cancellationToken));
    }
}

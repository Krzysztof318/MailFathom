// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.ObjectModel;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>Reads the content of the emails one call names, from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// The use case owns the whole path from identifiers to readable messages: it establishes that each email belongs to an
/// account this deployment serves, decides whether content exists to read at all, verifies that what is stored is what
/// was written, has it rendered, and turns a damaged local copy into a stable per-email failure and a durable repair
/// request.
/// </para>
/// <para>
/// A read names several emails and answers for each of them separately, because one identifier this deployment cannot
/// serve must not discard the content of the others. What it refuses outright is the request rather than an email: a
/// list naming nothing, a list longer than one call serves, and a list naming the same email twice are all decided
/// before anything is read.
/// </para>
/// <para>
/// Two bounds stand between a caller and a mailbox, and both are the deployment's rather than the request's: each body
/// representation is bounded on its own, and the call as a whole has a character budget spent in the order the emails
/// were named. Everything they cut says which bound cut it, so a caller can tell a message worth reading alone from a
/// batch worth splitting. Attachments are subject to neither, because no octet of one is ever returned here.
/// </para>
/// <para>
/// What a read hands back for a file is a short-lived link to fetch it, minted only where the request asked for one and
/// only where the deployment is configured to issue any. The link is a bearer capability, so minting it is an act rather
/// than a projection: it is the one thing this use case produces that did not already exist in the mailbox.
/// </para>
/// <para>
/// It reaches no mail server. That is the acceptance criterion this operation exists under rather than a property it
/// happens to have: reading mail must never download it and must never set the remote <c>\Seen</c> flag, so a missing
/// local copy is answered with a failure and a repair request instead of a fetch. The use case holds no mailbox port,
/// which is what makes the guarantee structural rather than a rule someone has to keep.
/// </para>
/// </remarks>
public sealed class EmailContentReader
{
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailContentRenderer renderer;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly IMailAccountCatalog accountCatalog;
    private readonly IAttachmentDownloadLinkIssuer linkIssuer;
    private readonly EmailContentReadOptions readOptions;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="renderer">Turns stored raw MIME into headers, a body, and a description of every attachment.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <param name="linkIssuer">Mints the short-lived capability a caller fetches an attachment with.</param>
    /// <param name="readOptions">The bounds on how much body text one call returns.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailContentReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore repairRequestStore,
        IMailAccountCatalog accountCatalog,
        IAttachmentDownloadLinkIssuer linkIssuer,
        EmailContentReadOptions readOptions)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(linkIssuer);
        ArgumentNullException.ThrowIfNull(readOptions);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.renderer = renderer;
        this.repairRequestStore = repairRequestStore;
        this.accountCatalog = accountCatalog;
        this.linkIssuer = linkIssuer;
        this.readOptions = readOptions;
    }

    /// <summary>Reads every email the request names.</summary>
    /// <param name="request">The emails to read and which representations to produce.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One outcome per named email, in the order they were named.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Reading writes nothing about the emails themselves and is safe to repeat. The one write it can perform is the
    /// repair request a damaged local copy produces, which is idempotent per email for exactly that reason.
    /// </para>
    /// <para>
    /// The emails are read one after another rather than concurrently, because the budget is carried from one to the
    /// next: what an email is allowed to return depends on what the emails before it returned, and that is what makes
    /// the bound on a whole call exact rather than approximate.
    /// </para>
    /// </remarks>
    public async Task<GetEmailContentResult> ReadContentAsync(
        GetEmailContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var outcomes = new List<EmailContentReadOutcome>(request.StoredEmailIds.Count);
        var remainingCharacters = this.readOptions.MaxCharactersPerRead;

        foreach (var storedEmailId in request.StoredEmailIds)
        {
            var outcome = await this.ReadOneAsync(
                storedEmailId,
                request,
                remainingCharacters,
                cancellationToken);

            remainingCharacters -= CharactersReturnedBy(outcome);
            outcomes.Add(outcome);
        }

        return new GetEmailContentResult(new ReadOnlyCollection<EmailContentReadOutcome>(outcomes));
    }

    /// <summary>Reads one email, or reports why it could not be.</summary>
    private async Task<EmailContentReadOutcome> ReadOneAsync(
        StoredEmailId storedEmailId,
        GetEmailContentRequest request,
        int remainingCharacters,
        CancellationToken cancellationToken)
    {
        var summary = await this.summaryReader.FindAsync(storedEmailId, cancellationToken);

        // An account this deployment no longer serves leaves its stored rows in place, so the row existing is not
        // enough. The two cases produce one answer: telling them apart would let a caller learn which identifiers
        // exist by asking about them.
        if (summary is null || !this.accountCatalog.ServedAccounts.Any(account => account.Id == summary.AccountId))
        {
            return EmailContentReadOutcome.NotFound(storedEmailId);
        }

        // Both states are content synchronization deliberately did not store, so neither is a damaged local copy and
        // neither schedules a repair. They are answered apart because only one of them is worth asking about again.
        if (BodyOfUnstoredContent(summary.ContentAvailability) is { } unstoredBody)
        {
            return EmailContentReadOutcome.Read(ContentWithoutStoredMime(summary, unstoredBody));
        }

        var content = await this.contentStore.FindStoredContentAsync(storedEmailId, cancellationToken);
        if (content is null)
        {
            return await this.RequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            return await this.RequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        var rendering = await this.renderer.RenderAsync(
            content,
            new EmailContentRenderingBounds(
                request.IncludeSanitizedHtml,
                this.readOptions.MaxBodyCharacters,
                remainingCharacters),
            cancellationToken);

        if (rendering.Rendering is not { } rendered)
        {
            return await this.RequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        var attachments = await this.DescribeAttachmentsAsync(
            summary.StoredEmailId,
            rendered.Attachments,
            request.IncludeAttachmentDownloadLinks,
            cancellationToken);

        return EmailContentReadOutcome.Read(ContentFrom(summary, rendered, attachments));
    }

    /// <summary>Pairs each described attachment with the way its content is reached, or with the reason it is not.</summary>
    /// <remarks>
    /// The links are minted for the whole message in one call, because the signing material behind them is resolved per
    /// operation and erased with it. A message carrying no attachment mints nothing at all, which keeps a read of
    /// ordinary mail from touching the key ring however the request was written.
    /// </remarks>
    private async Task<IReadOnlyList<ReadEmailAttachment>> DescribeAttachmentsAsync(
        StoredEmailId storedEmailId,
        IReadOnlyList<ExtractedEmailAttachment> attachments,
        bool includeDownloadLinks,
        CancellationToken cancellationToken)
    {
        if (!includeDownloadLinks)
        {
            return [.. attachments.Select(description =>
                new ReadEmailAttachment(description, AttachmentDownload.NotRequested))];
        }

        if (!this.linkIssuer.CanIssueLinks)
        {
            return [.. attachments.Select(description =>
                new ReadEmailAttachment(description, AttachmentDownload.Unavailable))];
        }

        if (attachments.Count == 0)
        {
            return [];
        }

        var links = await this.linkIssuer.IssueAsync(storedEmailId, attachments.Count, cancellationToken);

        return [.. attachments.Zip(
            links,
            (description, link) => new ReadEmailAttachment(description, AttachmentDownload.Issued(link)))];
    }

    /// <summary>Counts what one outcome drew from the read's character budget.</summary>
    /// <remarks>
    /// Both representations count, because both are message content a caller received. What is counted is the text as
    /// returned rather than the length the message held, so a body the per-representation bound already cut spends only
    /// what it actually published.
    /// </remarks>
    private static int CharactersReturnedBy(EmailContentReadOutcome outcome) =>
        outcome.Content is { } content
            ? content.Body.PlainText.Text.Length + (content.Body.SanitizedHtml?.Text.Length ?? 0)
            : 0;

    /// <summary>Records the defect durably and produces the outcome to report for it.</summary>
    /// <remarks>
    /// The request is recorded before the outcome is produced, so the finding survives whether or not the caller acts on
    /// what comes back.
    /// </remarks>
    private async Task<EmailContentReadOutcome> RequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return EmailContentReadOutcome.ContentUnavailable(summary.StoredEmailId, defect);
    }

    /// <summary>Builds the content of an email whose stored MIME was read.</summary>
    /// <remarks>
    /// The counts come from the same parse as the list rather than from the row, so the two can never disagree. They
    /// would for a message stored before extraction ran: its row records no attachments until the backfill reaches it,
    /// while the message it describes has them, and reporting the row's answer beside a list of two files would be
    /// wrong in the one direction a caller cannot check.
    /// <para>
    /// The counts and the descriptions are published whatever the caller asked for, because both are what a caller
    /// decides against: how many files there are, and whether any of them is worth fetching. Only the link is the
    /// request's to ask for, which is why the attachments arrive here already paired with what was minted for them.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentFrom(
        EmailSummary summary,
        EmailContentRendering rendering,
        IReadOnlyList<ReadEmailAttachment> attachments)
    {
        return new ReadEmailContent
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            SizeOctets = summary.SizeOctets,
            Headers = rendering.Headers,
            Body = rendering.BodyIsEncrypted
                ? EmailContentBody.EncryptedNotReadableLocally
                : EmailContentBody.Readable(rendering.PlainTextBody, rendering.SanitizedHtmlBody),
            AttachmentSummary = SummaryOf(rendering.AttachmentSummary),
            Attachments = attachments,
            RemoteFlags = summary.RemoteFlags,
        };
    }

    private static StoredEmailAttachmentSummary SummaryOf(EmailAttachmentSummary attachments) => new(
        attachments.AttachmentCount,
        attachments.TotalSizeOctets,
        attachments.InlineResourceCount,
        attachments.IsEncrypted,
        attachments.CarriesUnverifiedSignature,
        attachments.ContainsUnexpandedTnefPart);

    /// <summary>Names the body to report for an occurrence whose content was never stored, or nothing when it was.</summary>
    /// <remarks>
    /// A row recorded as available and holding no content is a different thing entirely — that is a local copy that has
    /// gone missing, and the caller answers it with a repair request rather than with a body state.
    /// </remarks>
    private static EmailContentBody? BodyOfUnstoredContent(StoredEmailContentAvailability availability) =>
        availability switch
        {
            StoredEmailContentAvailability.ExceededSizeLimit => EmailContentBody.NotStoredExceededSizeLimit,
            StoredEmailContentAvailability.AwaitingStorageHeadroom => EmailContentBody.NotStoredAwaitingStorageHeadroom,
            _ => null,
        };

    /// <summary>Builds the content of an email whose raw MIME synchronization deliberately kept out of local storage.</summary>
    /// <remarks>
    /// Everything answerable is still answered, and nothing else is. The headers come from the columns the listing is
    /// served out of, which are narrower than a parse would produce but are what exists. Both the per-attachment list
    /// and the attachment counts are absent, because nobody has ever read this message's parts: the row carries what
    /// the server's envelope reported, and an envelope says nothing about attachments, so its zero counts are unset
    /// defaults rather than a finding. The body state says why all of it is missing, and which of the two reasons it
    /// was.
    /// <para>
    /// The empty attachment list is therefore about this message's parts never having been read rather than about it
    /// carrying no files, which the absent counts beside it state.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentWithoutStoredMime(EmailSummary summary, EmailContentBody body)
    {
        return new ReadEmailContent
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            SizeOctets = summary.SizeOctets,
            Headers = HeadersFrom(summary),
            Body = body,
            AttachmentSummary = null,
            Attachments = [],
            RemoteFlags = summary.RemoteFlags,
        };
    }

    private static EmailContentHeaders HeadersFrom(EmailSummary summary) => new(
        summary.Subject,
        summary.SentAt,
        summary.ReceivedAt,
        ParticipantsFrom(summary),
        EmailThreadReferences.Create(summary.InternetMessageId, inReplyTo: null, references: null));

    /// <summary>Rebuilds the participants a listing row can answer for, which is the sender and the direct addressees.</summary>
    /// <remarks>
    /// The row keeps the comparison forms a filter needs, so the display names of the addressees and every
    /// <c>Cc</c>, <c>Reply-To</c>, <c>Bcc</c>, and <c>Sender</c> distinction are absent here. That is a narrower reading
    /// of the message than a parse produces, and it is the honest one: nothing local holds what was not stored.
    /// </remarks>
    private static ReadOnlyCollection<EmailParticipant> ParticipantsFrom(EmailSummary summary) =>
        new List<EmailParticipant>(
        [
            .. ParticipantOrNone(EmailAddressRole.From, summary.SenderDisplayName, summary.SenderAddress),
            .. summary.ToAddresses.SelectMany(address =>
                ParticipantOrNone(EmailAddressRole.To, displayName: null, address)),
        ]).AsReadOnly();

    private static IEnumerable<EmailParticipant> ParticipantOrNone(
        EmailAddressRole role,
        string? displayName,
        string? address) =>
        EmailAddress.TryCreate(displayName, address, out var emailAddress)
            ? [new EmailParticipant(role, emailAddress)]
            : [];
}

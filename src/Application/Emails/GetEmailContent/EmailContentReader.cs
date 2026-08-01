// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
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
/// Two bounds stand between a caller and a mailbox, and both are the deployment's rather than the request's. Each body
/// representation is bounded on its own, and the call as a whole has a character budget spent in the order the emails
/// were named. Every representation states which of the two cut it, so a caller can tell a message worth reading alone
/// from a batch worth splitting.
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
    private readonly EmailContentReadOptions readOptions;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="renderer">Turns stored raw MIME into headers, a body, and attachment metadata.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <param name="readOptions">The bounds on how much body text one call returns.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailContentReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore repairRequestStore,
        IMailAccountCatalog accountCatalog,
        EmailContentReadOptions readOptions)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(readOptions);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.renderer = renderer;
        this.repairRequestStore = repairRequestStore;
        this.accountCatalog = accountCatalog;
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
    /// The emails are read one after another rather than concurrently, because the character budget is carried from one
    /// to the next: what an email is allowed to return depends on what the emails before it returned, and that is what
    /// makes the bound on a whole call exact rather than approximate.
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
            var outcome = await this.ReadOneAsync(storedEmailId, request, remainingCharacters, cancellationToken);

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
        if (summary is null || !this.accountCatalog.ServedAccountIds.Contains(summary.AccountId))
        {
            return EmailContentReadOutcome.NotFound(storedEmailId);
        }

        if (summary.ContentAvailability is StoredEmailContentAvailability.ExceededSizeLimit)
        {
            return EmailContentReadOutcome.Read(ContentWithoutStoredMime(summary, request));
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

        return rendering.Rendering is { } rendered
            ? EmailContentReadOutcome.Read(ContentFrom(summary, rendered, request))
            : await this.RequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
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
    /// The counts are published whether or not the caller asked to describe the attachments, because how many a message
    /// carries is what tells a caller that asking again would describe something. Only the names, media types, and sizes
    /// are withheld.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentFrom(
        EmailSummary summary,
        EmailContentRendering rendering,
        GetEmailContentRequest request)
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
            AttachmentSummary = SummaryOf(rendering.Attachments),
            Attachments = request.IncludeAttachmentDetails ? rendering.Attachments.Attachments : null,
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

    /// <summary>Builds the content of an email whose raw MIME the size limit kept out of local storage.</summary>
    /// <remarks>
    /// Everything answerable is still answered, and nothing else is. The headers come from the columns the listing is
    /// served out of, which are narrower than a parse would produce but are what exists. Both the per-attachment list
    /// and the attachment counts are absent, because nobody has ever read this message's parts: the row carries what
    /// the server's envelope reported, and an envelope says nothing about attachments, so its zero counts are unset
    /// defaults rather than a finding. The body state says why all of it is missing.
    /// <para>
    /// The empty list a caller that asked for attachment descriptions receives is therefore about this message's
    /// content being unread rather than about it carrying no files, which the absent counts beside it state.
    /// </para>
    /// </remarks>
    private static ReadEmailContent ContentWithoutStoredMime(
        EmailSummary summary,
        GetEmailContentRequest request)
    {
        return new ReadEmailContent
        {
            StoredEmailId = summary.StoredEmailId,
            AccountId = summary.AccountId,
            FolderAlias = summary.FolderAlias,
            SizeOctets = summary.SizeOctets,
            Headers = HeadersFrom(summary),
            Body = EmailContentBody.NotStoredExceededSizeLimit,
            AttachmentSummary = null,
            Attachments = request.IncludeAttachmentDetails ? [] : null,
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

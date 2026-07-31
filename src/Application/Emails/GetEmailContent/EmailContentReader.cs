// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>Reads one email's content from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// The use case owns the whole path from an identifier to a readable message: it establishes that the email belongs to
/// an account this deployment serves, decides whether content exists to read at all, verifies that what is stored is
/// what was written, has it rendered, and turns a damaged local copy into a stable failure and a durable repair
/// request.
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

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="renderer">Turns stored raw MIME into headers, a body, and attachment metadata.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="accountCatalog">Answers which accounts this deployment serves.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailContentReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailContentRenderer renderer,
        IEmailContentRepairRequestStore repairRequestStore,
        IMailAccountCatalog accountCatalog)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(accountCatalog);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.renderer = renderer;
        this.repairRequestStore = repairRequestStore;
        this.accountCatalog = accountCatalog;
    }

    /// <summary>Reads one email.</summary>
    /// <param name="request">The email to read and which representations to produce.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The email's headers, body, and attachment metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="StoredEmailNotFoundException">Thrown when the local mailbox copy holds no such email, or holds it for an account this deployment does not serve.</exception>
    /// <exception cref="EmailContentUnavailableException">Thrown when the email exists and its stored content is missing, damaged, or unreadable. A repair request is recorded before it is raised.</exception>
    /// <remarks>
    /// Reading writes nothing about the email itself and is safe to repeat. The one write it can perform is the repair
    /// request a damaged local copy produces, which is idempotent per email for exactly that reason.
    /// </remarks>
    public async Task<GetEmailContentResult> ReadContentAsync(
        GetEmailContentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var summary = await this.summaryReader.FindAsync(request.StoredEmailId, cancellationToken);

        // An account this deployment no longer serves leaves its stored rows in place, so the row existing is not
        // enough. The two cases produce one answer: telling them apart would let a caller learn which identifiers
        // exist by asking about them.
        if (summary is null || !this.accountCatalog.ServedAccountIds.Contains(summary.AccountId))
        {
            throw new StoredEmailNotFoundException(request.StoredEmailId);
        }

        if (summary.ContentAvailability is StoredEmailContentAvailability.ExceededSizeLimit)
        {
            return ResultWithoutStoredContent(summary);
        }

        var content = await this.contentStore.FindStoredContentAsync(request.StoredEmailId, cancellationToken);
        if (content is null)
        {
            throw await this.RequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            throw await this.RequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        var rendering = await this.renderer.RenderAsync(content, request.IncludeSanitizedHtml, cancellationToken);
        if (rendering.Rendering is not { } rendered)
        {
            throw await this.RequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        return ResultFrom(summary, rendered);
    }

    /// <summary>Records the defect durably and produces the failure to raise for it.</summary>
    /// <remarks>
    /// The request is recorded before the failure is raised, so the finding survives whether or not anything catches
    /// what comes back. Returning the exception rather than throwing it keeps the <c>throw</c> at the call site, where
    /// a reader of this use case can see that each of these paths ends the operation.
    /// </remarks>
    private async Task<EmailContentUnavailableException> RequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return new EmailContentUnavailableException(summary.StoredEmailId, defect);
    }

    /// <summary>Builds the result of an email whose stored MIME was read.</summary>
    /// <remarks>
    /// The counts come from the same parse as the list rather than from the row, so the two can never disagree. They
    /// would for a message stored before extraction ran: its row records no attachments until the backfill reaches it,
    /// while the message it describes has them, and reporting the row's answer beside a list of two files would be
    /// wrong in the one direction a caller cannot check.
    /// </remarks>
    private static GetEmailContentResult ResultFrom(EmailSummary summary, EmailContentRendering rendering) => new()
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
        Attachments = rendering.Attachments.Attachments,
        RemoteFlags = summary.RemoteFlags,
    };

    private static StoredEmailAttachmentSummary SummaryOf(EmailAttachmentSummary attachments) => new(
        attachments.AttachmentCount,
        attachments.TotalSizeOctets,
        attachments.InlineResourceCount,
        attachments.IsEncrypted,
        attachments.CarriesUnverifiedSignature,
        attachments.ContainsUnexpandedTnefPart);

    /// <summary>Builds the result for an email whose raw MIME the size limit kept out of local storage.</summary>
    /// <remarks>
    /// Everything answerable is still answered, and nothing else is. The headers come from the columns the listing is
    /// served out of, which are narrower than a parse would produce but are what exists. Both the per-attachment list
    /// and the attachment counts are absent, because nobody has ever read this message's parts: the row carries what
    /// the server's envelope reported, and an envelope says nothing about attachments, so its zero counts are unset
    /// defaults rather than a finding. The body state says why all of it is missing.
    /// </remarks>
    private static GetEmailContentResult ResultWithoutStoredContent(EmailSummary summary) => new()
    {
        StoredEmailId = summary.StoredEmailId,
        AccountId = summary.AccountId,
        FolderAlias = summary.FolderAlias,
        SizeOctets = summary.SizeOctets,
        Headers = HeadersFrom(summary),
        Body = EmailContentBody.NotStoredExceededSizeLimit,
        AttachmentSummary = null,
        Attachments = [],
        RemoteFlags = summary.RemoteFlags,
    };

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

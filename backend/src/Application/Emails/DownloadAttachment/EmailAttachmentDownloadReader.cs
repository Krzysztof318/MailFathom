// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.DownloadAttachment;

/// <summary>Opens one attachment of one stored email, from the local mailbox copy.</summary>
/// <remarks>
/// <para>
/// Two callers reach it and each states its own authorization, because the two ways an attachment is asked for are
/// genuinely different acts. <see cref="OpenAsync" /> serves a link this deployment signed, which names the attachment
/// and is the whole of the access control. <see cref="OpenForReaderAsync" /> serves a caller that authenticated and
/// holds <see cref="MailFathomPermission.MailRead" />, which is a person opening a file in their own mailbox. What
/// neither of them may do is decide the rest: everything an authorization cannot establish — that the email exists,
/// that it belongs to an account this deployment currently serves and this owner owns, that the stored copy is what
/// was written, and that the message really carries a part at that position — is settled here for both, once.
/// </para>
/// <para>
/// It resolves through the live store on every request rather than against anything staged when a link was minted. An
/// attachment is mail content in full and inherits every retention, access, and erasure constraint of the message it
/// belongs to, so a link must not be able to outlive the deletion of its own message — which is a property of reading it
/// afresh rather than a rule anybody has to remember.
/// </para>
/// <para>
/// Every refusal is the same refusal. A message this deployment no longer serves, one belonging to somebody else, a
/// stored copy that has gone missing, a damaged one, and a position the message does not have all answer
/// <see langword="null" />, because telling them apart would let a caller learn what became of mail they cannot read —
/// and, on the signed path, learn it while holding nothing but a URL.
/// </para>
/// <para>
/// It reaches no mail server, for the reason the content read does not: the use case holds no mailbox port, so a
/// download cannot fetch a message and cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class EmailAttachmentDownloadReader
{
    private readonly IStoredEmailSummaryReader summaryReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailAttachmentContentReader attachmentContentReader;
    private readonly IEmailContentRepairRequestStore repairRequestStore;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case.</summary>
    /// <param name="summaryReader">Reads one stored email's summary by its identity.</param>
    /// <param name="contentStore">Reads the raw MIME stored for an email, with what was recorded about it.</param>
    /// <param name="attachmentContentReader">Opens one attachment of that stored MIME by its position.</param>
    /// <param name="repairRequestStore">Records durably that a local copy has to be fetched or read again.</param>
    /// <param name="scopeResolver">Answers whether a tool may read the mailbox an email was stored from.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmailAttachmentDownloadReader(
        IStoredEmailSummaryReader summaryReader,
        IEmailContentStore contentStore,
        IEmailAttachmentContentReader attachmentContentReader,
        IEmailContentRepairRequestStore repairRequestStore,
        MailboxScopeResolver scopeResolver,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(summaryReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(attachmentContentReader);
        ArgumentNullException.ThrowIfNull(repairRequestStore);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(authorization);

        this.summaryReader = summaryReader;
        this.contentStore = contentStore;
        this.attachmentContentReader = attachmentContentReader;
        this.repairRequestStore = repairRequestStore;
        this.scopeResolver = scopeResolver;
        this.authorization = authorization;
    }

    /// <summary>Opens the attachment the ticket authorizes.</summary>
    /// <param name="ticket">What the redeemed capability authorizes.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The opened attachment, which the caller owns and must dispose, or <see langword="null" /> when there is nothing to serve.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ticket" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached under anything but a capability this deployment signed.</exception>
    /// <remarks>
    /// <para>
    /// The principal is asked for before the ticket is acted on, and it is asked for here rather than only at the route,
    /// so that an entrypoint added later cannot reach an attachment by holding a mailbox grant. A capability is the
    /// authorization in full — it names one attachment of one email and expires — which is why this entrypoint admits
    /// that kind alone and asks for no permission beside it. A caller holding a grant is served by
    /// <see cref="OpenForReaderAsync" /> instead, and neither of the two admits the other's principal.
    /// </para>
    /// <para>
    /// A damaged or missing local copy records a repair request before the refusal, exactly as reading the message's
    /// content does. The finding is about the stored copy rather than about who asked for it, so discarding it because
    /// the request happened to arrive through a link would leave a defect discovered and unrecorded. A payload the
    /// database answered for because its object could not be vouched for records one too, and the download proceeds.
    /// </para>
    /// </remarks>
    public async Task<IOpenedEmailAttachment?> OpenAsync(
        AttachmentDownloadTicket ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        this.authorization.RequireSignedCapability();

        return await this.OpenPartAsync(ticket.StoredEmailId, ticket.AttachmentPosition, cancellationToken);
    }

    /// <summary>Opens one attachment for a caller that authenticated and holds the mailbox read grant.</summary>
    /// <param name="storedEmailId">The email whose attachment to open, as a read of that email published it.</param>
    /// <param name="attachmentPosition">The attachment's zero-based position in the order a read of that email lists them.</param>
    /// <param name="cancellationToken">Cancels the read when the reader disconnects.</param>
    /// <returns>The opened attachment, which the caller owns and must dispose, or <see langword="null" /> when there is nothing to serve.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" />.</exception>
    /// <remarks>
    /// <para>
    /// Both values arrive from the caller rather than from a signature this deployment wrote, so neither is trusted:
    /// the email is resolved against the accounts the caller's owner owns and the folders a read may reach, and the
    /// position is what the message's own walk answers for or does not. That is the same resolution the signed path
    /// runs, which is why a position naming nothing is a refusal here rather than a validation failure.
    /// </para>
    /// <para>
    /// The grant is asked for here rather than only at the transport, so an entrypoint added later cannot reach an
    /// attachment by forgetting a filter. It is the same grant reading the message itself is published under, because
    /// a file the sender attached is part of the message a reader already holds rather than a wider disclosure.
    /// </para>
    /// </remarks>
    public async Task<IOpenedEmailAttachment?> OpenForReaderAsync(
        StoredEmailId storedEmailId,
        int attachmentPosition,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailRead);

        return await this.OpenPartAsync(storedEmailId, attachmentPosition, cancellationToken);
    }

    /// <summary>Opens the attachment both entrypoints resolved to, having each established its own caller.</summary>
    /// <remarks>
    /// Everything below this line is the same whoever asked, which is the reason it is one method: the checks are about
    /// the mail rather than about the request, and a second copy of them would be a second place for one of them to be
    /// left out.
    /// </remarks>
    private async Task<IOpenedEmailAttachment?> OpenPartAsync(
        StoredEmailId storedEmailId,
        int attachmentPosition,
        CancellationToken cancellationToken)
    {
        var summary = await this.summaryReader.FindAsync(storedEmailId, cancellationToken);

        // A ticket outlives the configuration it was minted under and a caller names an email it read at some earlier
        // moment, so what either of them reaches is re-decided here: an account the deployment stopped serving, one
        // belonging to another owner, and a folder an operator withheld from tools all answer with nothing, exactly as
        // an email that is no longer stored does.
        if (summary is null || !this.scopeResolver.IsReadableByTools(summary.AccountId, summary.FolderAlias))
        {
            return null;
        }

        var content = await this.contentStore.FindStoredContentAsync(storedEmailId, cancellationToken);
        if (content is null)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Missing, cancellationToken);
        }

        if (content.FindIntegrityDefect() is { } integrityDefect)
        {
            return await this.RefuseAndRequestRepairAsync(summary, integrityDefect, cancellationToken);
        }

        await this.repairRequestStore.NoteIfServedFromRetainedCopyAsync(
            content,
            summary.StoredEmailId,
            cancellationToken);

        var opened = await this.attachmentContentReader.OpenAsync(
            content,
            attachmentPosition,
            cancellationToken);

        if (opened.ContentIsUnreadable)
        {
            return await this.RefuseAndRequestRepairAsync(summary, EmailContentDefect.Unreadable, cancellationToken);
        }

        return opened.Attachment;
    }

    /// <summary>Records the defect durably and refuses the download.</summary>
    private async Task<IOpenedEmailAttachment?> RefuseAndRequestRepairAsync(
        EmailSummary summary,
        EmailContentDefect defect,
        CancellationToken cancellationToken)
    {
        await this.repairRequestStore.RecordAsync(
            new EmailContentRepairRequest(summary.StoredEmailId, defect),
            cancellationToken);

        return null;
    }
}
